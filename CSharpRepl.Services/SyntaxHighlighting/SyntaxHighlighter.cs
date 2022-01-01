// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Classification;
using Microsoft.CodeAnalysis.Text;
using Microsoft.Extensions.Caching.Memory;
using PrettyPrompt.Highlighting;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace CSharpRepl.Services.SyntaxHighlighting;

/// <summary>
/// Invokes roslyn's classification API on a code document, and combines the
/// classifications with a theme file to determine the resulting spans of color.
/// </summary>
internal sealed class SyntaxHighlighter
{
    private const string CacheKeyPrefix = "SyntaxHighlighter_";
    private readonly IReadOnlyDictionary<string, AnsiColor> theme;
    private readonly AnsiColor unhighlightedColor;
    private readonly IReadOnlyDictionary<string, AnsiColor> ansiColorNames;
    private readonly MemoryCache cache;

    public SyntaxHighlighter(MemoryCache cache, string? themeName)
    {
        this.cache = cache;
        this.ansiColorNames = new Dictionary<string, AnsiColor>
            {
                { nameof(AnsiColor.Black), AnsiColor.Black },
                { nameof(AnsiColor.BrightWhite), AnsiColor.BrightWhite },
                { nameof(AnsiColor.BrightCyan), AnsiColor.BrightCyan },
                { nameof(AnsiColor.BrightMagenta), AnsiColor.BrightMagenta },
                { nameof(AnsiColor.BrightYellow), AnsiColor.BrightYellow },
                { nameof(AnsiColor.BrightGreen), AnsiColor.BrightGreen },
                { nameof(AnsiColor.BrightRed), AnsiColor.BrightRed },
                { nameof(AnsiColor.BrightBlack), AnsiColor.BrightBlack },
                { nameof(AnsiColor.BrightBlue), AnsiColor.BrightBlue },
                { nameof(AnsiColor.Cyan), AnsiColor.Cyan },
                { nameof(AnsiColor.Magenta), AnsiColor.Magenta },
                { nameof(AnsiColor.Blue), AnsiColor.Blue },
                { nameof(AnsiColor.Yellow), AnsiColor.Yellow },
                { nameof(AnsiColor.Green), AnsiColor.Green },
                { nameof(AnsiColor.Red), AnsiColor.Red },
                { nameof(AnsiColor.White), AnsiColor.White },
            };

        var selectedTheme = string.IsNullOrEmpty(themeName)
            ? new DefaultTheme()
            : JsonSerializer.Deserialize<Theme>(
                File.ReadAllText(themeName),
                new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }
              ) ?? new DefaultTheme();
        this.theme = selectedTheme
            .Colors
            .ToDictionary(t => t.Name, t => ToAnsiColor(t.Foreground));
        this.unhighlightedColor = theme["text"];
    }

    private AnsiColor ToAnsiColor(string foreground)
    {
        var span = foreground.AsSpan();
        if (foreground.StartsWith('#') && foreground.Length == 7
            && byte.TryParse(span.Slice(1, 2), NumberStyles.AllowHexSpecifier, null, out byte r)
            && byte.TryParse(span.Slice(3, 2), NumberStyles.AllowHexSpecifier, null, out byte g)
            && byte.TryParse(span.Slice(5, 2), NumberStyles.AllowHexSpecifier, null, out byte b))
        {
            return AnsiColor.RGB(r, g, b);
        }

        if (ansiColorNames.TryGetValue(foreground, out AnsiColor? color))
        {
            return color;
        }

        throw new ArgumentException(@$"Unknown recognized color ""{foreground}"". Expecting either a hexadecimal color of the format #RRGGBB or a standard ANSI color name");
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
                    .Select(classification => theme.GetValueOrDefault(classification.ClassificationType))
                    .FirstOrDefault(themeColor => themeColor is not null)
                    ?? unhighlightedColor;
                return new HighlightedSpan(classifications.Key, highlight);
            })
            .ToList();

        this.cache.Set(cacheKey, highlighted, DateTimeOffset.Now.AddMinutes(1));

        return highlighted;
    }

    internal AnsiColor GetColor(string keyword) => theme.GetValueOrDefault(keyword, unhighlightedColor);
}
