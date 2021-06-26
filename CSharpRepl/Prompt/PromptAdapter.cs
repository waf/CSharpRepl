// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Collections.Generic;
using System.Linq;
using CSharpRepl.Services.SyntaxHighlighting;
using PrettyPrompt.Highlighting;
using PromptCompletionItem = PrettyPrompt.Completion.CompletionItem;
using CSharpRepl.Services.Completion;

namespace CSharpRepl.Prompt
{
    /// <summary>
    /// Maps the classes produced by our roslyn code into the classes for interacting with our prompt library.
    /// </summary>
    internal sealed class PromptAdapter
    {
        public IReadOnlyCollection<FormatSpan> AdaptSyntaxClassification(IReadOnlyCollection<HighlightedSpan> classifications) =>
            classifications
                .Select(r => new FormatSpan(
                    r.TextSpan.Start,
                    r.TextSpan.Length,
                    new ConsoleFormat(Foreground: r.Color)
                ))
                .Where(f => f.Formatting is not null)
                .ToArray();

        public IReadOnlyList<PromptCompletionItem> AdaptCompletions(IReadOnlyCollection<CompletionItemWithDescription> completions) =>
            completions
                .OrderByDescending(i => i.Item.Rules.MatchPriority)
                .Select(r => new PromptCompletionItem
                {
                    StartIndex = r.Item.Span.Start,
                    ReplacementText = r.Item.DisplayText,
                    DisplayText = r.Item.DisplayTextPrefix + r.Item.DisplayText + r.Item.DisplayTextSuffix,
                    ExtendedDescription = r.DescriptionProvider
                })
                .ToArray();
    }
}
