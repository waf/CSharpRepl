// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CSharpRepl.Services.Theming;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Classification;
using Microsoft.CodeAnalysis.Text;
using Microsoft.Extensions.Caching.Memory;
using PrettyPrompt.Highlighting;

namespace CSharpRepl.Services.SyntaxHighlighting;

/// <summary>
/// Invokes roslyn's classification API on a code document, and combines the
/// classifications with a theme file to determine the resulting spans of color.
/// </summary>
internal sealed class SyntaxHighlighter
{
    private const string CacheKeyPrefix = "SyntaxHighlighter_";
    private readonly Theme theme;
    private readonly AnsiColor unhighlightedColor;
    private readonly MemoryCache cache;

    public SyntaxHighlighter(MemoryCache cache, Theme theme)
    {
        this.cache = cache;
        this.theme = theme;
        this.unhighlightedColor = theme.GetSyntaxHighlightingAnsiColor("text", AnsiColor.White);
    }

    internal async Task<IReadOnlyCollection<HighlightedSpan>> HighlightAsync(Document document)
    {
        var text = (await document.GetTextAsync()).ToString();
        var cacheKey = CacheKeyPrefix + document.Name + text;
        if (this.cache.Get<IReadOnlyCollection<HighlightedSpan>>(cacheKey) is IReadOnlyCollection<HighlightedSpan> spans)
            return spans;

        var classified = await Classifier.GetClassifiedSpansAsync(document, TextSpan.FromBounds(0, text.Length)).ConfigureAwait(false);

        // we can have multiple classifications for a given span. Choose the first one that has a corresponding color in the theme.
        var highlighted = classified
            .GroupBy(classification => classification.TextSpan)
            .Select(classifications =>
            {
                var highlight = classifications
                    .Select(classification => theme.GetSyntaxHighlightingAnsiColor(classification.ClassificationType))
                    .FirstOrDefault(themeColor => themeColor is not null)
                    ?? unhighlightedColor;
                return new HighlightedSpan(classifications.Key, highlight);
            })
            .ToList();

        this.cache.Set(cacheKey, highlighted, DateTimeOffset.Now.AddMinutes(1));

        return highlighted;
    }

    internal AnsiColor GetColor(string keyword) => theme.GetSyntaxHighlightingAnsiColor(keyword, unhighlightedColor);
    internal bool TryGetColor(string keyword, out AnsiColor color) => theme.TryGetSyntaxHighlightingAnsiColor(keyword, out color);
}