using Microsoft.CodeAnalysis.Classification;
using PrettyPrompt.Highlighting;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace ReplDotNet.SyntaxHighlighting
{
    class SyntaxHighlighter
    {
        private readonly IReadOnlyDictionary<string, AnsiColor> theme;
        private readonly AnsiColor unhighlightedColor;
        private readonly IReadOnlyDictionary<string, AnsiColor> ansiColorNames;

        public SyntaxHighlighter(string themeName)
        {
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

            var selectedTheme = themeName is null
                ? new DefaultTheme()
                : JsonSerializer.Deserialize<Theme>(File.ReadAllText(themeName));
            this.theme = selectedTheme
                .colors
                .ToDictionary(t => t.name, t => ToAnsiColor(t.foreground));
            this.unhighlightedColor = theme["plain text"];
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

            if (ansiColorNames.TryGetValue(foreground, out AnsiColor color))
            {
                return color;
            }

            throw new ArgumentException(@$"Unknown recognized color ""{foreground}"". Expecting either a hexadecimal color of the format #RRGGBB or a standard ANSI color name");
        }

        internal IReadOnlyCollection<HighlightedSpan> Highlight(IReadOnlyCollection<ClassifiedSpan> classified)
        {
            // we can have multiple classifications for a given span. Choose the first one that has a corresponding color in the theme.
            var highlighted = classified
                .GroupBy(classification => classification.TextSpan) 
                .Select(classifications =>
                {
                    var highlight = classifications
                        .Select(classification => theme.GetValueOrDefault(classification.ClassificationType, null))
                        .FirstOrDefault(themeColor => themeColor is not null)
                        ?? unhighlightedColor;
                    return new HighlightedSpan(classifications.Key, highlight);
                })
                .ToList();

            return highlighted;
        }

        internal AnsiColor GetColor(string keyword) => theme[keyword];
    }
}
