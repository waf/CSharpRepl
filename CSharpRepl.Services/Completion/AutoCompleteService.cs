// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Completion;
using Microsoft.CodeAnalysis.QuickInfo;
using Microsoft.Extensions.Caching.Memory;
using PrettyPrompt.Highlighting;
using System;
using System.Linq;
using System.Threading.Tasks;

using PrettyPromptCompletionItem = PrettyPrompt.Completion.CompletionItem;

namespace CSharpRepl.Services.Completion;

public record CompletionItemWithDescription(CompletionItem Item, PrettyPromptCompletionItem.GetExtendedDescriptionHandler GetDescriptionAsync);

internal sealed class AutoCompleteService
{
    private const string CacheKeyPrefix = "AutoCompleteService_";
    private readonly IMemoryCache cache;

    public AutoCompleteService(IMemoryCache cache)
    {
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
            .Select(item => new CompletionItemWithDescription(item, cancellationToken => GetExtendedDescription(document, item)))
            .ToArray() ?? Array.Empty<CompletionItemWithDescription>();

        cache.Set(cacheKey, completionsWithDescriptions, DateTimeOffset.Now.AddMinutes(1));

        return completionsWithDescriptions;
    }

    private static async Task<FormattedString> GetExtendedDescription(Document document, CompletionItem item)
    {
        var currentText = await document.GetTextAsync().ConfigureAwait(false);
        var completedText = currentText.Replace(item.Span, item.DisplayText);
        var completedDocument = document.WithText(completedText);

        var infoService = QuickInfoService.GetService(completedDocument);
        if (infoService is null) return string.Empty;

        var info = await infoService.GetQuickInfoAsync(completedDocument, item.Span.End).ConfigureAwait(false);
        return info is null
            ? string.Empty
            : string.Join(Environment.NewLine, info.Sections.Select(s => s.Text));
    }
}
