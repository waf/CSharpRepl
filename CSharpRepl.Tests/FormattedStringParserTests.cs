// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Collections.Generic;
using CSharpRepl.Services.Theming;
using PrettyPrompt.Highlighting;
using Xunit;

namespace CSharpRepl.Tests;

public class FormattedStringParserTests
{
    [Theory]
    [InlineData("", "")]
    [InlineData(" ", " ")]
    [InlineData("abc", "abc")]
    [InlineData("abc def", "abc def")]
    [InlineData("ab(cd)", "ab(cd)")]
    [InlineData("[[", "[")]
    [InlineData("ab[[cd]]", "ab[cd]")]
    [InlineData("/", "/")]
    public void ParseNonFormatted(string pattern, string expectedResult)
    {
        Assert.True(FormattedStringParser.TryParse(pattern, out var result));
        Assert.Equal(0, result.FormatSpans.Count);
        Assert.Equal(expectedResult, result.Text);
    }

    [Theory]
    [InlineData("[")]
    [InlineData("]")]
    [InlineData("[]")]
    [InlineData("[a")]
    [InlineData("]a")]
    [InlineData("[]a")]
    [InlineData("a[")]
    [InlineData("a]")]
    [InlineData("a[]")]
    [InlineData("[red][blue]a")]
    [InlineData("[red]a[blue]")]
    [InlineData("ab[[cd]")]
    [InlineData("ab[cd]]")]
    [InlineData("[red]")]
    [InlineData("[red]a")]
    [InlineData("[/]")]
    [InlineData("a[/]")]
    [InlineData("[red][/][/]")]
    [InlineData("[on][/]")]
    [InlineData("[red on][/]")]
    [InlineData("[on on red][/]")]
    public void ParseBroken(string pattern)
    {
        Assert.False(FormattedStringParser.TryParse(pattern, out var result));
        Assert.Equal(0, result.FormatSpans.Count);
        Assert.True(result.IsEmpty);
    }

    [Theory]
    [MemberData(nameof(ParseStyleData))]
    public void ParseStyle(string pattern, FormattedString expectedResult)
    {
        Assert.Equal(expectedResult, FormattedStringParser.Parse(pattern));
    }

    public static IEnumerable<object[]> ParseStyleData
    {
        get
        {
            yield return new object[] { "[red][/]", FormattedString.Empty };
            yield return new object[] { "[red]a[/]", new FormattedString("a", new FormatSpan(0, 1, AnsiColor.Red)) };
            yield return new object[] { "[red]a[/]b", new FormattedString("ab", new FormatSpan(0, 1, AnsiColor.Red)) };
            yield return new object[] { "[red]a[/][green]b[/]", new FormattedString("ab", new FormatSpan(0, 1, AnsiColor.Red), new FormatSpan(1, 1, AnsiColor.Green)) };

            yield return new object[] { "[red bold]a[/]", new FormattedString("a", new FormatSpan(0, 1, new ConsoleFormat(Foreground: AnsiColor.Red, Bold: true))) };
            yield return new object[] { "[red underline]a[/]", new FormattedString("a", new FormatSpan(0, 1, new ConsoleFormat(Foreground: AnsiColor.Red, Underline: true))) };
            yield return new object[] { "[red inverted]a[/]", new FormattedString("a", new FormatSpan(0, 1, new ConsoleFormat(Foreground: AnsiColor.Red, Inverted: true))) };
            yield return new object[] { "[red bold underline inverted]a[/]", new FormattedString("a", new FormatSpan(0, 1, new ConsoleFormat(Foreground: AnsiColor.Red, Bold: true, Underline: true, Inverted: true))) };

            yield return new object[] { "[on red]a[/]", new FormattedString("a", new FormatSpan(0, 1, new ConsoleFormat(Background: AnsiColor.Red))) };
            yield return new object[] { "[blue on red]a[/]", new FormattedString("a", new FormatSpan(0, 1, new ConsoleFormat(Foreground: AnsiColor.Blue, Background: AnsiColor.Red))) };
            yield return new object[] { "[bold blue on red]a[/]", new FormattedString("a", new FormatSpan(0, 1, new ConsoleFormat(Foreground: AnsiColor.Blue, Background: AnsiColor.Red, Bold: true))) };
            yield return new object[] { "[blue on red bold]a[/]", new FormattedString("a", new FormatSpan(0, 1, new ConsoleFormat(Foreground: AnsiColor.Blue, Background: AnsiColor.Red, Bold: true))) };
            yield return new object[] { "[red]a[/][on green]b[/]", new FormattedString("ab", new FormatSpan(0, 1, AnsiColor.Red), new FormatSpan(1, 1, new ConsoleFormat(Background: AnsiColor.Green))) };

            yield return new object[] { "[red][[a]][/][on green][[b]][/]", new FormattedString("[a][b]", new FormatSpan(0, 3, AnsiColor.Red), new FormatSpan(3, 3, new ConsoleFormat(Background: AnsiColor.Green))) };
            yield return new object[] { "[bold]Usage[/]: [[OPTIONS]]", new FormattedString("Usage: [OPTIONS]", new FormatSpan(0, 5, new ConsoleFormat(Bold: true))) };
        }
    }
}