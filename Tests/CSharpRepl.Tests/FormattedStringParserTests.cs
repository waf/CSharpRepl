// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using CSharpRepl.Services.Theming;
using Xunit;

namespace CSharpRepl.Tests;

// FormattedStringParser delegates parsing to Spectre.Console's markup parser (AnsiMarkup) and maps the
// parsed Style onto PrettyPrompt's ConsoleFormat. Standard colors map to PrettyPrompt's named palette
// AnsiColors (so they follow the terminal theme); #RRGGBB colors are kept as truecolor RGB.
public class FormattedStringParserTests
{
    [Theory]
    [InlineData("", "")]
    [InlineData(" ", " ")]
    [InlineData("abc", "abc")]
    [InlineData("abc def", "abc def")]
    [InlineData("ab(cd)", "ab(cd)")]
    [InlineData("/", "/")]
    [InlineData("[[", "[")]
    [InlineData("ab[[cd]]", "ab[cd]")]
    public void ParseNonFormatted(string pattern, string expectedResult)
    {
        Assert.True(FormattedStringParser.TryParse(pattern, out var result));
        Assert.Equal(0, result.FormatSpans.Length);
        Assert.Equal(expectedResult, result.Text);
    }

    [Theory]
    [InlineData("[")]                 // unterminated tag
    [InlineData("[red]a[blue]b")]     // unterminated tag (Spectre requires the closing ']')
    [InlineData("[/]")]               // closing tag with no opening
    [InlineData("a[/]")]              // closing tag with no opening
    [InlineData("[red]a[/][/]")]      // extra closing tag
    [InlineData("[notacolor]a[/]")]   // unknown color
    public void ParseBroken(string pattern)
    {
        Assert.False(FormattedStringParser.TryParse(pattern, out var result));
        Assert.Equal(0, result.FormatSpans.Length);
        Assert.True(result.IsEmpty);
    }

    [Fact]
    public void ParseStyle_AppliesForegroundOverRange_RemainderUnformatted()
    {
        Assert.True(FormattedStringParser.TryParse("[red]a[/]b", out var result));
        Assert.Equal("ab", result.Text);
        Assert.Equal(1, result.FormatSpans.Length);
        Assert.Equal(0, result.FormatSpans[0].Start);
        Assert.Equal(1, result.FormatSpans[0].Length);
        Assert.NotNull(result.FormatSpans[0].Formatting.Foreground);
    }

    [Fact]
    public void ParseStyle_MultipleSpans()
    {
        Assert.True(FormattedStringParser.TryParse("[red]a[/][green]b[/]", out var result));
        Assert.Equal("ab", result.Text);
        Assert.Equal(2, result.FormatSpans.Length);
        Assert.Equal(0, result.FormatSpans[0].Start);
        Assert.Equal(1, result.FormatSpans[1].Start);
    }

    [Fact]
    public void ParseStyle_EmptyContent_ProducesEmpty()
    {
        Assert.True(FormattedStringParser.TryParse("[red][/]", out var result));
        Assert.True(result.IsEmpty);
    }

    [Theory]
    [InlineData("[red bold]a[/]", true, false, false)]
    [InlineData("[red underline]a[/]", false, true, false)]
    [InlineData("[red invert]a[/]", false, false, true)]
    [InlineData("[red bold underline invert]a[/]", true, true, true)]
    public void ParseStyle_Decorations(string pattern, bool bold, bool underline, bool inverted)
    {
        Assert.True(FormattedStringParser.TryParse(pattern, out var result));
        Assert.Equal(1, result.FormatSpans.Length);
        var format = result.FormatSpans[0].Formatting;
        Assert.Equal(bold, format.Bold);
        Assert.Equal(underline, format.Underline);
        Assert.Equal(inverted, format.Inverted);
    }

    [Fact]
    public void ParseStyle_ForegroundAndBackground()
    {
        Assert.True(FormattedStringParser.TryParse("[blue on red]a[/]", out var result));
        Assert.Equal(1, result.FormatSpans.Length);
        var format = result.FormatSpans[0].Formatting;
        Assert.NotNull(format.Foreground);
        Assert.NotNull(format.Background);
    }

    [Fact]
    public void ParseStyle_EscapedBracketsInsideStyle()
    {
        Assert.True(FormattedStringParser.TryParse("[red][[a]][/]", out var result));
        Assert.Equal("[a]", result.Text);
        Assert.Equal(1, result.FormatSpans.Length);
        Assert.Equal(0, result.FormatSpans[0].Start);
        Assert.Equal(3, result.FormatSpans[0].Length);
    }

    [Fact]
    public void StandardColor_MapsToTerminalPalette_NotAbsoluteRgb()
    {
        // A standard color must become a palette AnsiColor (which the terminal themes), so it stays
        // readable, rather than an absolute truecolor value. Palette colors stringify to a friendly
        // name; RGB colors stringify to "#RRGGBB".
        Assert.True(FormattedStringParser.TryParse("[blue]a[/]", out var result));
        var foreground = result.FormatSpans[0].Formatting.Foreground;
        Assert.NotNull(foreground);
        Assert.DoesNotContain("#", foreground.ToString());
    }

    [Fact]
    public void HexColor_PreservedAsTrueColorRgb()
    {
        Assert.True(FormattedStringParser.TryParse("[#1A2B3C]a[/]", out var result));
        var foreground = result.FormatSpans[0].Formatting.Foreground;
        Assert.NotNull(foreground);
        Assert.Equal("#1A2B3C", foreground.ToString());
    }
}
