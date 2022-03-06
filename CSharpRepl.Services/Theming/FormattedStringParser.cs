using System;
using System.Linq;
using System.Text.RegularExpressions;
using PrettyPrompt.Highlighting;

namespace CSharpRepl.Services.Theming;

internal static class FormattedStringParser
{
    private static readonly Regex StyleTagRegex = new(
            @"\[[a-z \#0-9]*\]|\[/\]",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex UnescapedTextCheckerRegex = new(
        @"[^\[]\[[^\[]|" +  // x[x 
        @"[^\]]\][^\]]|" +  // x]x
        @"^\[[^\[]|" +      //^[x
        @"^\][^\]]|" +      //^]x
        @"[^\]]\]$|" +      // x]$
        @"[^\[]\[$|" +      // x[$
        @"^\[$|" +          //^[$
        @"^\]$",            //^]$
        RegexOptions.Compiled);

    public static bool TryParse(string input, out FormattedString result)
    {
        result = FormattedString.Empty;

        var styleMatches = StyleTagRegex.Matches(input)
            .Where(m => StartsWithEvenNumberOf(input.AsSpan()[m.Index..], ']') && EndsWithEvenNumberOf(input.AsSpan(0, m.Index), '['))
            .ToList();

        if (styleMatches.Count % 2 != 0)
        {
            //some style tags is surely not closed
            return false;
        }

        if (styleMatches.Count == 0)
        {
            if (!ValidateUnescapedTextPart(input))
            {
                return false;
            }
            result = Unescape(input);
            return true;
        }

        var sb = new FormattedStringBuilder();
        ConsoleFormat? lastFormat = null;
        int lastFormatEnd = 0;
        foreach (Match styleMatch in styleMatches)
        {
            if (styleMatch.Length == 0)
            {
                return false;
            }

            var previousText = input[lastFormatEnd..styleMatch.Index];
            if (!ValidateUnescapedTextPart(previousText))
            {
                return false;
            }
            previousText = Unescape(previousText);
            lastFormatEnd += previousText.Length + styleMatch.Length;
            if (lastFormat.HasValue)
            {
                if (styleMatch.ValueSpan.Equals("[/]", StringComparison.Ordinal))
                {
                    sb.Append(new FormattedString(previousText, lastFormat.Value));
                    lastFormat = null;
                    continue;
                }
                else
                {
                    //previous style tag wasn't closed
                    return false;
                }
            }
            else
            {
                sb.Append(previousText);

                if (!TryParseConsoleFormat(styleMatch.ValueSpan[1..^1].ToString(), out var format))
                {
                    return false;
                }
                lastFormat = format;
            }
        }

        if (lastFormat.HasValue)
        {
            //last style tag wasn't closed
            return false;
        }
        else
        {
            sb.Append(input[lastFormatEnd..]);
        }

        result = sb.ToFormattedString();
        return true;
    }

    private static bool TryParseConsoleFormat(string input, out ConsoleFormat result)
    {
        result = default;

        bool bold = false;
        bool underline = false;
        bool inverted = false;
        AnsiColor? foreground = null;
        AnsiColor? background = null;
        var parts = input.Split(' ');
        bool onKeywordActive = false;
        for (int i = 0; i < parts.Length; i++)
        {
            var part = parts[i];
            if (part.Length == 0) return false;

            if (part.Equals("bold", StringComparison.OrdinalIgnoreCase))
            {
                if (onKeywordActive) return false;
                bold = true;
            }
            else if (part.Equals("underline", StringComparison.OrdinalIgnoreCase))
            {
                if (onKeywordActive) return false;
                underline = true;
            }
            else if (part.Equals("inverted", StringComparison.OrdinalIgnoreCase))
            {
                if (onKeywordActive) return false;
                inverted = true;
            }
            else
            {
                if (part.Equals("on", StringComparison.OrdinalIgnoreCase))
                {
                    if (onKeywordActive) return false;
                    onKeywordActive = true;
                }
                else if (onKeywordActive)
                {
                    if (background.HasValue ||
                        !Color.TryParseAnsiColor(part, out var color))
                    {
                        return false;
                    }
                    background = color;
                    onKeywordActive = false;
                }
                else
                {
                    if (foreground.HasValue ||
                        !Color.TryParseAnsiColor(part, out var color))
                    {
                        return false;
                    }
                    foreground = color;
                }
            }
        }

        if (onKeywordActive) return false;

        result = new ConsoleFormat(Foreground: foreground, Background: background, Bold: bold, Underline: underline, Inverted: inverted);
        return true;
    }

    private static string Unescape(string text)
        => text.Replace("[[", "[").Replace("]]", "]");

    private static bool ValidateUnescapedTextPart(string input)
        => !UnescapedTextCheckerRegex.IsMatch(input);

    private static bool StartsWithEvenNumberOf(ReadOnlySpan<char> text, char c)
    {
        int count = 0;
        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] == c) count++;
            else break;
        }
        return count % 2 == 0;
    }

    private static bool EndsWithEvenNumberOf(ReadOnlySpan<char> text, char c)
    {
        int count = 0;
        for (int i = text.Length - 1; i >= 0; i--)
        {
            if (text[i] == c) count++;
            else break;
        }
        return count % 2 == 0;
    }
}