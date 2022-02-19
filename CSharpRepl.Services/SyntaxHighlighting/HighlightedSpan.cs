// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Microsoft.CodeAnalysis.Text;
using PrettyPrompt.Highlighting;
using System.Collections.Generic;
using System.Linq;

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
}