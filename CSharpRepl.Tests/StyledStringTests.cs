// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using CSharpRepl.Services.Theming;
using Spectre.Console;
using Xunit;

namespace CSharpRepl.Tests;

public class StyledStringTests
{
    [Fact]
    public void SubstringSimple()
    {
        var redStyle = new Style(foreground: Color.Red);
        foreach (var style in new[] { null, redStyle })
        {
            var text = new StyledString("abcd", style);

            var subtext = text.Substring(0, 4);
            Assert.Equal("abcd", subtext.ToString());
            Assert.Single(subtext.Parts);
            Assert.Equal(style, subtext.Parts[0].Style);

            subtext = text.Substring(0, 2);
            Assert.Equal("ab", subtext.ToString());
            Assert.Single(subtext.Parts);
            Assert.Equal(style, subtext.Parts[0].Style);

            subtext = text.Substring(1, 2);
            Assert.Equal("bc", subtext.ToString());
            Assert.Single(subtext.Parts);
            Assert.Equal(style, subtext.Parts[0].Style);

            subtext = text.Substring(2, 2);
            Assert.Equal("cd", subtext.ToString());
            Assert.Single(subtext.Parts);
            Assert.Equal(style, subtext.Parts[0].Style);

            subtext = text.Substring(2, 0);
            Assert.Equal("", subtext.ToString());
            Assert.Empty(subtext.Parts);
        }
    }

    [Fact]
    public void SubstringComplex()
    {
        var redStyle = new Style(foreground: Color.Red);
        var blueStyle = new Style(foreground: Color.Blue);
        var text = new StyledString(new[] { new StyledStringSegment("ab", redStyle), new StyledStringSegment("cd", null), new StyledStringSegment("ef", blueStyle) });

        var subtext = text.Substring(0, 6);
        Assert.Equal("abcdef", subtext.ToString());
        Assert.Equal(3, subtext.Parts.Count);
        Assert.Equal(redStyle, subtext.Parts[0].Style);
        Assert.Null(subtext.Parts[1].Style);
        Assert.Equal(blueStyle, subtext.Parts[2].Style);

        subtext = text.Substring(0, 5);
        Assert.Equal("abcde", subtext.ToString());
        Assert.Equal(3, subtext.Parts.Count);
        Assert.Equal(redStyle, subtext.Parts[0].Style);
        Assert.Null(subtext.Parts[1].Style);
        Assert.Equal(blueStyle, subtext.Parts[2].Style);

        subtext = text.Substring(0, 4);
        Assert.Equal("abcd", subtext.ToString());
        Assert.Equal(2, subtext.Parts.Count);
        Assert.Equal(redStyle, subtext.Parts[0].Style);
        Assert.Null(subtext.Parts[1].Style);

        subtext = text.Substring(0, 3);
        Assert.Equal("abc", subtext.ToString());
        Assert.Equal(2, subtext.Parts.Count);
        Assert.Equal(redStyle, subtext.Parts[0].Style);
        Assert.Null(subtext.Parts[1].Style);

        subtext = text.Substring(0, 2);
        Assert.Equal("ab", subtext.ToString());
        Assert.Single(subtext.Parts);
        Assert.Equal(redStyle, subtext.Parts[0].Style);

        subtext = text.Substring(0, 1);
        Assert.Equal("a", subtext.ToString());
        Assert.Single(subtext.Parts);
        Assert.Equal(redStyle, subtext.Parts[0].Style);

        ///////////////////////////////////////////////////////////////////////

        subtext = text.Substring(1, 5);
        Assert.Equal("bcdef", subtext.ToString());
        Assert.Equal(3, subtext.Parts.Count);
        Assert.Equal(redStyle, subtext.Parts[0].Style);
        Assert.Null(subtext.Parts[1].Style);
        Assert.Equal(blueStyle, subtext.Parts[2].Style);

        subtext = text.Substring(2, 4);
        Assert.Equal("cdef", subtext.ToString());
        Assert.Equal(2, subtext.Parts.Count);
        Assert.Null(subtext.Parts[0].Style);
        Assert.Equal(blueStyle, subtext.Parts[1].Style);

        subtext = text.Substring(3, 3);
        Assert.Equal("def", subtext.ToString());
        Assert.Equal(2, subtext.Parts.Count);
        Assert.Null(subtext.Parts[0].Style);
        Assert.Equal(blueStyle, subtext.Parts[1].Style);

        subtext = text.Substring(4, 2);
        Assert.Equal("ef", subtext.ToString());
        Assert.Single(subtext.Parts);
        Assert.Equal(blueStyle, subtext.Parts[0].Style);

        subtext = text.Substring(5, 1);
        Assert.Equal("f", subtext.ToString());
        Assert.Single(subtext.Parts);
        Assert.Equal(blueStyle, subtext.Parts[0].Style);

        ///////////////////////////////////////////////////////////////////////

        subtext = text.Substring(1, 4);
        Assert.Equal("bcde", subtext.ToString());
        Assert.Equal(3, subtext.Parts.Count);
        Assert.Equal(redStyle, subtext.Parts[0].Style);
        Assert.Null(subtext.Parts[1].Style);
        Assert.Equal(blueStyle, subtext.Parts[2].Style);
    }
}