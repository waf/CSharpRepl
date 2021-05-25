using System;
using System.Collections.Generic;
using System.Linq;
using Sharply.Services.Roslyn;
using Sharply.Services.SyntaxHighlighting;
using PrettyPrompt.Highlighting;
using PromptCompletionItem = PrettyPrompt.Completion.CompletionItem;

namespace Sharply.Prompt
{
    /// <summary>
    /// Maps the classes produced by our roslyn code into the classes for interacting with our prompt library.
    /// </summary>
    sealed class PromptAdapter
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
                    //DisplayText = r.Item.DisplayTextPrefix + r.Item.DisplayText + r.Item.DisplayTextSuffix,
                    ExtendedDescription = r.DescriptionProvider
                })
                .ToArray()
                ??
                Array.Empty<PromptCompletionItem>();
    }
}
