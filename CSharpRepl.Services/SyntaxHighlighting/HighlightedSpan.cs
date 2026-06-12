// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis.Text;
using PrettyPrompt.Highlighting;

namespace CSharpRepl.Services.SyntaxHighlighting;

public sealed record HighlightedSpan(TextSpan TextSpan, AnsiColor Color);

public static class HighlightedSpanExtensions
{
    public static IReadOnlyCollection<FormatSpan> ToFormatSpans(this IReadOnlyCollection<HighlightedSpan> spans) => spans
        .Select(span => new FormatSpan(
            span.TextSpan.Start,
            span.TextSpan.Length,
            new ConsoleFormat(Foreground: span.Color)
        ))
        .ToArray();

    /// <summary>
    /// Converts possibly-overlapping format spans into the disjoint spans <see cref="FormattedString"/> requires.
    /// Roslyn's classifier nests spans (e.g. a string-escape-character span inside a string-literal span). The
    /// later-starting span is the more specific classification, so it wins; the span it interrupts resumes after
    /// it ends. The prompt's own renderer tolerates overlap, so this is only needed when building a FormattedString.
    /// </summary>
    public static IReadOnlyList<FormatSpan> FlattenOverlappingSpans(this IEnumerable<FormatSpan> spans)
    {
        // at equal starts, the longer (enclosing) span sorts first so the shorter, more specific span is
        // pushed later and takes precedence over it.
        var sorted = spans.OrderBy(s => s.Start).ThenByDescending(s => s.Length).ToArray();
        var result = new List<FormatSpan>(sorted.Length);
        var enclosing = new Stack<FormatSpan>();
        int position = 0;

        foreach (var span in sorted)
        {
            // emit the tails of spans that end before this one starts
            while (enclosing.TryPeek(out var top) && top.End <= span.Start)
            {
                Emit(top, top.End);
                enclosing.Pop();
            }
            // emit the segment of the still-open span leading up to this more specific span
            if (enclosing.TryPeek(out var current))
            {
                Emit(current, span.Start);
            }
            enclosing.Push(span);
        }
        while (enclosing.TryPop(out var top))
        {
            Emit(top, top.End);
        }
        return result;

        void Emit(FormatSpan span, int end)
        {
            int start = Math.Max(position, span.Start);
            if (end > start)
            {
                result.Add(new FormatSpan(start, end - start, span.Formatting));
                position = end;
            }
        }
    }
}