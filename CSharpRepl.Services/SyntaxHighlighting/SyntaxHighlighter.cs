// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading.Tasks;
using CSharpRepl.Services.Theming;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Classification;
using Microsoft.CodeAnalysis.Text;
using Microsoft.Extensions.Caching.Memory;
using PrettyPrompt.Highlighting;
using Spectre.Console;

namespace CSharpRepl.Services.SyntaxHighlighting;

/// <summary>
/// Invokes roslyn's classification API on a code document, and combines the
/// classifications with a theme file to determine the resulting spans of color.
/// </summary>
internal sealed class SyntaxHighlighter
{
    private const string CacheKeyPrefix = "SyntaxHighlighter_";
    private readonly Theme theme;
    private readonly AnsiColor unhighlightedAnsiColor;
    private readonly Color unhighlightedSpectreColor;
    private readonly MemoryCache cache;

    public SyntaxHighlighter(MemoryCache cache, Theme theme)
    {
        this.cache = cache;
        this.theme = theme;
        this.unhighlightedAnsiColor = theme.GetSyntaxHighlightingAnsiColor("text", AnsiColor.White);
        this.unhighlightedSpectreColor = theme.GetSyntaxHighlightingSpectreColor("text", Color.White);
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
                    ?? unhighlightedAnsiColor;
                return new HighlightedSpan(classifications.Key, highlight);
            })
            .ToList();

        this.cache.Set(cacheKey, highlighted, DateTimeOffset.Now.AddMinutes(1));

        return highlighted;
    }

    public AnsiColor GetAnsiColor(string? classification) => theme.GetSyntaxHighlightingAnsiColor(classification, unhighlightedAnsiColor);
    public bool TryGetAnsiColor(string? classification, out AnsiColor color) => theme.TryGetSyntaxHighlightingAnsiColor(classification, out color);
    public ConsoleFormat GetFormat(string? classification) => new(Foreground: GetAnsiColor(classification));
    public bool TryGetFormat(string? classification, out ConsoleFormat format)
    {
        if (theme.TryGetSyntaxHighlightingAnsiColor(classification, out var color))
        {
            format = new ConsoleFormat(Foreground: color);
            return true;
        }
        else
        {
            format = ConsoleFormat.None;
            return false;
        }
    }

    public Color GetSpectreColor(string? classification) => theme.GetSyntaxHighlightingSpectreColor(classification, unhighlightedSpectreColor);
    public bool TryGetSpectreColor(string? classification, out Color color) => theme.TryGetSyntaxHighlightingSpectreColor(classification, out color);
    public Style GetStyle(string? classification) => new(foreground: GetSpectreColor(classification));
    public bool TryGetStyle(string? classification, [NotNullWhen(true)] out Style? style)
    {
        if (theme.TryGetSyntaxHighlightingSpectreColor(classification, out var color))
        {
            style = new Style(foreground: color);
            return true;
        }
        else
        {
            style = null;
            return false;
        }
    }

    public Style KeywordStyle => GetStyle(ClassificationTypeNames.Keyword);
    public ConsoleFormat KeywordFormat => GetFormat(ClassificationTypeNames.Keyword);
}