// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using CSharpRepl.Services.SyntaxHighlighting;
using PrettyPrompt.Highlighting;
using Xunit;
using static PrettyPrompt.Highlighting.AnsiColor;

namespace CSharpRepl.Tests.SyntaxHighlighting;

public class FlattenOverlappingSpansTests
{
    private static FormatSpan Span(int start, int length, AnsiColor color) => new(start, length, new ConsoleFormat(Foreground: color));

    [Fact]
    public void NestedSpan_SplitsTheEnclosingSpanAroundIt()
    {
        // models a string literal (0..10) containing an escape sequence (3..5): the escape color wins
        // in the middle, and the literal's color resumes after it.
        var flattened = new[] { Span(0, 10, Red), Span(3, 2, Magenta) }.FlattenOverlappingSpans();

        Assert.Equal(new[] { Span(0, 3, Red), Span(3, 2, Magenta), Span(5, 5, Red) }, flattened);
    }

    [Fact]
    public void PartiallyOverlappingSpan_LaterStartingSpanWins()
    {
        var flattened = new[] { Span(0, 5, Red), Span(3, 5, Magenta) }.FlattenOverlappingSpans();

        Assert.Equal(new[] { Span(0, 3, Red), Span(3, 5, Magenta) }, flattened);
    }

    [Fact]
    public void DisjointSpans_AreReturnedSorted()
    {
        var flattened = new[] { Span(6, 2, Magenta), Span(0, 4, Red) }.FlattenOverlappingSpans();

        Assert.Equal(new[] { Span(0, 4, Red), Span(6, 2, Magenta) }, flattened);
    }

    [Fact]
    public void SameStart_ShorterMoreSpecificSpanWins()
    {
        var flattened = new[] { Span(0, 8, Red), Span(0, 3, Magenta) }.FlattenOverlappingSpans();

        Assert.Equal(new[] { Span(0, 3, Magenta), Span(3, 5, Red) }, flattened);
    }

    [Fact]
    public void MultipleNestedSpans_EachInterruptAndResumeTheEnclosingSpan()
    {
        // a literal with two escape sequences, e.g. "a\nb\tc"
        var flattened = new[] { Span(0, 12, Red), Span(2, 2, Magenta), Span(7, 2, Magenta) }.FlattenOverlappingSpans();

        Assert.Equal(
            new[] { Span(0, 2, Red), Span(2, 2, Magenta), Span(4, 3, Red), Span(7, 2, Magenta), Span(9, 3, Red) },
            flattened);
    }
}
