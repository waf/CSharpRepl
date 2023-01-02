// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using CSharpRepl.Services.Extensions;
using CSharpRepl.Services.SyntaxHighlighting;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Classification;
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

    public async Task<CompletionItemWithDescription[]> Complete(Document document, string text, int caret)
    {
        var cacheKey = CacheKeyPrefix + document.Name + text + caret;
        if (text != string.Empty && cache.Get<CompletionItemWithDescription[]>(cacheKey) is CompletionItemWithDescription[] cached)
            return cached;

        var completionService = CompletionService.GetService(document);
        if (completionService is null) return Array.Empty<CompletionItemWithDescription>();

        var completions = await completionService
            .GetCompletionsAsync(document, caret)
            .ConfigureAwait(false);

        var completionsWithDescriptions = completions?.ItemsList
            .Select(item => new CompletionItemWithDescription(item, GetDisplayText(item), cancellationToken => GetExtendedDescriptionAsync(completionService, document, item, highlighter)))
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
                    highlighter.TryGetAnsiColor(classification, out var color))
                {
                    var prefix = GetCompletionItemSymbolPrefix(classification, configuration.UseUnicode);
                    return new FormattedString($"{prefix}{text}", new FormatSpan(prefix.Length, text.Length, new ConsoleFormat(Foreground: color)));
                }
            }
            return text;
        }
    }

    public static string GetCompletionItemSymbolPrefix(string? classification, bool useUnicode)
    {
        Span<char> prefix = stackalloc char[3];
        if (useUnicode)
        {
            var symbol = classification switch
            {
                ClassificationTypeNames.Keyword => "🔑",
                ClassificationTypeNames.MethodName or ClassificationTypeNames.ExtensionMethodName => "🟣",
                ClassificationTypeNames.PropertyName => "🟡",
                ClassificationTypeNames.FieldName or ClassificationTypeNames.ConstantName or ClassificationTypeNames.EnumMemberName => "🔵",
                ClassificationTypeNames.EventName => "⚡",
                ClassificationTypeNames.ClassName or ClassificationTypeNames.RecordClassName => "🟨",
                ClassificationTypeNames.InterfaceName => "🔷",
                ClassificationTypeNames.StructName or ClassificationTypeNames.RecordStructName => "🟦",
                ClassificationTypeNames.EnumName => "🟧",
                ClassificationTypeNames.DelegateName => "💼",
                ClassificationTypeNames.NamespaceName => "⬜",
                ClassificationTypeNames.TypeParameterName => "⬛",
                _ => "⚫",
            };
            Debug.Assert(symbol.Length <= prefix.Length);
            symbol.CopyTo(prefix);
            prefix[symbol.Length] = ' ';
            prefix = prefix[..(symbol.Length + 1)];
            return prefix.ToString();
        }
        else
        {
            return "";
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
            if (classification is not null &&
                highlighter.TryGetAnsiColor(classification, out var color))
            {
                stringBuilder.Append(taggedText.Text, new FormatSpan(0, taggedText.Text.Length, new ConsoleFormat(Foreground: color)));
            }
            else
            {
                stringBuilder.Append(taggedText.Text);
            }
        }
        return stringBuilder.ToFormattedString();
    }
}