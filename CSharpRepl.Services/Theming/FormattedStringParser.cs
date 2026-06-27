// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using PrettyPrompt.Highlighting;
using Spectre.Console;

namespace CSharpRepl.Services.Theming;

/// <summary>
/// Parses Spectre.Console markup (e.g. <c>[aqua bold]text[/]</c>, <c>[blue on red]text[/]</c>, with
/// <c>[[</c> / <c>]]</c> escaping a literal bracket) into a PrettyPrompt <see cref="FormattedString"/>.
/// Spectre does the actual parsing via <see cref="AnsiMarkup.Parse"/>; this only maps each parsed
/// segment's <see cref="Style"/> onto PrettyPrompt's <see cref="ConsoleFormat"/>. A FormattedString
/// (rather than a Spectre renderable) is produced because PrettyPrompt renders the interactive prompt,
/// and the console output helpers take FormattedStrings.
/// </summary>
public static class FormattedStringParser
{
    public static FormattedString Parse(string input)
    {
        if (!TryParse(input, out var result))
        {
            throw new ArgumentException("Unable to parse formatted string.", nameof(input));
        }
        return result;
    }

    public static bool TryParse(string input, out FormattedString result)
    {
        try
        {
            var builder = new FormattedStringBuilder();
            foreach (var segment in AnsiMarkup.Parse(input, style: null))
            {
                if (segment.Text.Length == 0)
                {
                    continue;
                }
                else if (segment.Style == Style.Plain)
                {
                    builder.Append(segment.Text);
                }
                else
                {
                    var format = ToConsoleFormat(segment.Style);
                    builder.Append(new FormattedString(segment.Text, format));
                }
            }
            result = builder.ToFormattedString();
            return true;
        }
        catch (Exception)
        {
            // Spectre throws on malformed markup (unbalanced/unknown tags, unknown colors).
            result = FormattedString.Empty;
            return false;
        }
    }

    private static ConsoleFormat ToConsoleFormat(Style style)
    {
        var decoration = style.Decoration;
        return new ConsoleFormat(
            Foreground: ToAnsiColor(style.Foreground),
            Background: ToAnsiColor(style.Background),
            Bold: decoration.HasFlag(Decoration.Bold),
            Underline: decoration.HasFlag(Decoration.Underline),
            Inverted: decoration.HasFlag(Decoration.Invert));
    }

    // Spectre exposes only RGB (no palette index), so the standard 16 colors look like any other RGB
    // value. Emitting them as truecolor would bake in an absolute shade and ignore the user's terminal
    // theme (e.g. a hard-to-read [blue]). So detect a standard color by round-tripping through
    // ConsoleColor and map it to the matching named AnsiColor (a palette escape the terminal themes);
    // genuine #RRGGBB colors fall through to truecolor RGB.
    private static AnsiColor? ToAnsiColor(Color color)
    {
        if (color == Color.Default) return null;

        var consoleColor = Color.ToConsoleColor(color);
        if (Color.FromConsoleColor(consoleColor) == color)
        {
            return FromConsoleColor(consoleColor);
        }

        return AnsiColor.Rgb(color.R, color.G, color.B);
    }

    // Inverse of ThemeColor.TryConvertAnsiColorToConsoleColor: ConsoleColor's dark variants are the
    // base palette colors, the bright variants are the "Bright*" palette colors.
    internal static AnsiColor FromConsoleColor(ConsoleColor color) => color switch
    {
        ConsoleColor.Black => AnsiColor.Black,
        ConsoleColor.DarkRed => AnsiColor.Red,
        ConsoleColor.DarkGreen => AnsiColor.Green,
        ConsoleColor.DarkYellow => AnsiColor.Yellow,
        ConsoleColor.DarkBlue => AnsiColor.Blue,
        ConsoleColor.DarkMagenta => AnsiColor.Magenta,
        ConsoleColor.DarkCyan => AnsiColor.Cyan,
        ConsoleColor.Gray => AnsiColor.White,
        ConsoleColor.DarkGray => AnsiColor.BrightBlack,
        ConsoleColor.Red => AnsiColor.BrightRed,
        ConsoleColor.Green => AnsiColor.BrightGreen,
        ConsoleColor.Yellow => AnsiColor.BrightYellow,
        ConsoleColor.Blue => AnsiColor.BrightBlue,
        ConsoleColor.Magenta => AnsiColor.BrightMagenta,
        ConsoleColor.Cyan => AnsiColor.BrightCyan,
        ConsoleColor.White => AnsiColor.BrightWhite,
        _ => AnsiColor.White,
    };
}
