// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Linq;
using System.Threading.Tasks;
using CSharpRepl.Services.Extensions;
using CSharpRepl.Services.SyntaxHighlighting;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Completion;
using Microsoft.CodeAnalysis.QuickInfo;
using Microsoft.Extensions.Caching.Memory;
using PrettyPrompt.Highlighting;

using PrettyPromptCompletionItem = PrettyPrompt.Completion.CompletionItem;

namespace CSharpRepl.Services.Completion;

public record CompletionItemWithDescription(CompletionItem Item, FormattedString DisplayText, PrettyPromptCompletionItem.GetExtendedDescriptionHandler GetDescriptionAsync);

internal sealed class AutoCompleteService
{
    private const string CacheKeyPrefix = "AutoCompleteService_";

    private readonly SyntaxHighlighter highlighter;
    private readonly IMemoryCache cache;

    public AutoCompleteService(SyntaxHighlighter highlighter, IMemoryCache cache)
    {
        this.highlighter = highlighter;
        this.cache = cache;
    }

    public async Task<CompletionItemWithDescription[]> Complete(Document document, string text, int caret)
    {
        var cacheKey = CacheKeyPrefix + document.Name + text + caret;
        if (text != string.Empty && cache.Get<CompletionItemWithDescription[]>(cacheKey) is CompletionItemWithDescription[] cached)
            return cached;

        var completions = await CompletionService
            .GetService(document)
            .GetCompletionsAsync(document, caret)
            .ConfigureAwait(false);

        var completionsWithDescriptions = completions?.Items
            .Select(item => new CompletionItemWithDescription(item, GetDisplayText(item), cancellationToken => GetExtendedDescriptionAsync(document, item, highlighter)))
            .ToArray() ?? Array.Empty<CompletionItemWithDescription>();

        cache.Set(cacheKey, completionsWithDescriptions, DateTimeOffset.Now.AddMinutes(1));

        return completionsWithDescriptions;

        FormattedString GetDisplayText(CompletionItem item)
        {
            var text = item.DisplayTextPrefix + item.DisplayText + item.DisplayTextSuffix;
            if (item.Tags.Length > 0)
            {
                var classification = RoslynExtensions.TextTagToClassificationTypeName(item.Tags.First());
                if (classification is not null &&
                    highlighter.TryGetColor(classification, out var color))
                {
                    return new FormattedString(text, new FormatSpan(0, text.Length, new ConsoleFormat(Foreground: color)));
                }
            }
            return text;
        }
    }

    private static async Task<FormattedString> GetExtendedDescriptionAsync(Document document, CompletionItem item, SyntaxHighlighter highlighter)
    {
        var currentText = await document.GetTextAsync().ConfigureAwait(false);
        var completedText = currentText.Replace(item.Span, item.DisplayText);
        var completedDocument = document.WithText(completedText);

        var infoService = QuickInfoService.GetService(completedDocument);
        if (infoService is null) return string.Empty;

        var info = await infoService.GetQuickInfoAsync(completedDocument, item.Span.End).ConfigureAwait(false);
        if (info is null) return FormattedString.Empty;

        var stringBuilder = new FormattedStringBuilder();

        for (int sectionIndex = 0; sectionIndex < info.Sections.Length; sectionIndex++)
        {
            var section = info.Sections[sectionIndex];
            foreach (var taggedText in section.TaggedParts)
            {
                var classification = RoslynExtensions.TextTagToClassificationTypeName(taggedText.Tag);
                if (classification is not null &&
                    highlighter.TryGetColor(classification, out var color))
                {
                    stringBuilder.Append(taggedText.Text, new FormatSpan(0, taggedText.Text.Length, new ConsoleFormat(Foreground: color)));
                }
                else
                {
                    stringBuilder.Append(taggedText.Text);
                }
            }
            if (sectionIndex + 1 < info.Sections.Length) stringBuilder.Append(Environment.NewLine);
        }
        return stringBuilder.ToFormattedString();
    }
}