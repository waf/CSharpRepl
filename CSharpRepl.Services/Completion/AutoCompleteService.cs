// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CSharpRepl.Services.Extensions;
using CSharpRepl.Services.SyntaxHighlighting;
using CSharpRepl.Services.Theming;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Completion;
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
    private readonly Configuration configuration;

    public AutoCompleteService(SyntaxHighlighter highlighter, IMemoryCache cache, Configuration configuration)
    {
        this.highlighter = highlighter;
        this.cache = cache;
        this.configuration = configuration;
    }

    public async Task<CompletionItemWithDescription[]> Complete(Document document, string text, int caret, CancellationToken cancellationToken)
    {
        var cacheKey = CacheKeyPrefix + document.Name + text + caret;
        if (text != string.Empty && cache.Get<CompletionItemWithDescription[]>(cacheKey) is CompletionItemWithDescription[] cached)
            return cached;

        var completionService = CompletionService.GetService(document);
        if (completionService is null) return [];

        try
        {
            var completions = await completionService
                .GetCompletionsAsync(document, caret, cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            var completionsWithDescriptions = completions?.ItemsList
                .Where(item => item.DisplayText != nameof(__CSharpRepl_RuntimeHelper) && !(item.IsComplexTextEdit && item.InlineDescription.Length > 0)) //TODO https://github.com/waf/CSharpRepl/issues/236
                .Select(item => new CompletionItemWithDescription(item, GetDisplayText(item), cancellationToken => GetExtendedDescriptionAsync(completionService, document, item, highlighter)))
                .ToArray() ?? [];

            cache.Set(cacheKey, completionsWithDescriptions, DateTimeOffset.Now.AddMinutes(1));

            return completionsWithDescriptions;
        }
        catch (InvalidOperationException) // handle crashes from roslyn completion API
        {
            return [];
        }

        FormattedString GetDisplayText(CompletionItem item)
        {
            var text = item.DisplayTextPrefix + item.DisplayText + item.DisplayTextSuffix;
            if (item.Tags.Length > 0)
            {
                var classification = RoslynExtensions.TextTagToClassificationTypeName(item.Tags.First());
                if (highlighter.TryGetFormat(classification, out var format))
                {
                    var symbol = CompletionItemSymbols.Get(classification, configuration.UseUnicode);
                    var spans = new List<FormatSpan>(2);
                    if (CompletionItemSymbols.GetIconAnsiColor(symbol.Color) is { } iconColor)
                        spans.Add(new FormatSpan(0, symbol.GlyphLength, new ConsoleFormat(Foreground: iconColor)));
                    spans.Add(new FormatSpan(symbol.Prefix.Length, text.Length, format));
                    return new FormattedString($"{symbol.Prefix}{text}", spans);
                }
            }
            return text;
        }
    }

    private static async Task<FormattedString> GetExtendedDescriptionAsync(CompletionService completionService, Document document, CompletionItem item, SyntaxHighlighter highlighter)
    {
        var description = await completionService.GetDescriptionAsync(document, item);
        if (description is null) return string.Empty;

        var stringBuilder = new FormattedStringBuilder();
        foreach (var taggedText in description.TaggedParts)
        {
            var classification = RoslynExtensions.TextTagToClassificationTypeName(taggedText.Tag);
            if (highlighter.TryGetFormat(classification, out var format))
            {
                stringBuilder.Append(taggedText.Text, new FormatSpan(0, taggedText.Text.Length, format));
            }
            else
            {
                stringBuilder.Append(taggedText.Text);
            }
        }
        return stringBuilder.ToFormattedString();
    }
}