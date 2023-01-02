// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Reflection;
using PrettyPrompt.Highlighting;
using Spectre.Console;

namespace CSharpRepl.Services.Theming;

public readonly struct ThemeColor
{
    private static readonly Dictionary<string, AnsiColor> ansiColorNames =
        typeof(AnsiColor)
        .GetFields(BindingFlags.Static | BindingFlags.Public)
        .Where(f => f.FieldType == typeof(AnsiColor))
        .ToDictionary(f => f.Name, f => (AnsiColor)f.GetValue(null)!, StringComparer.OrdinalIgnoreCase);

    public ThemeColor(string color)
    {
        Debug.Assert(!string.IsNullOrEmpty(color));

        Value = color;
    }

    public string Value { get; }

    public AnsiColor ToAnsiColor()
    {
        if (TryParseAnsiColor(Value, out var color))
        {
            return color;
        }

        throw new ArgumentException($"Unknown recognized color '{Value}'. Expecting either a hexadecimal color of the format #RRGGBB or a standard ANSI color name");
    }

    public Color ToSpectreColor()
    {
        if (TryParseSpectreColor(Value, out var color))
        {
            return color;
        }

        throw new ArgumentException($"Unknown recognized color '{Value}'. Expecting either a hexadecimal color of the format #RRGGBB or a standard ANSI color name");
    }

    public static bool TryParseAnsiColor(string input, out AnsiColor result)
    {
        var span = input.AsSpan();
        if (input.StartsWith('#') && span.Length == 7 &&
            byte.TryParse(span.Slice(1, 2), NumberStyles.AllowHexSpecifier, null, out byte r) &&
            byte.TryParse(span.Slice(3, 2), NumberStyles.AllowHexSpecifier, null, out byte g) &&
            byte.TryParse(span.Slice(5, 2), NumberStyles.AllowHexSpecifier, null, out byte b))
        {
            result = AnsiColor.Rgb(r, g, b);
            return true;
        }

        if (ansiColorNames.TryGetValue(input, out var color))
        {
            result = color;
            return true;
        }

        result = default;
        return false;
    }

    public static bool TryParseSpectreColor(string input, out Color result)
    {
        var span = input.AsSpan();
        if (input.StartsWith('#') && span.Length == 7 &&
            byte.TryParse(span.Slice(1, 2), NumberStyles.AllowHexSpecifier, null, out byte r) &&
            byte.TryParse(span.Slice(3, 2), NumberStyles.AllowHexSpecifier, null, out byte g) &&
            byte.TryParse(span.Slice(5, 2), NumberStyles.AllowHexSpecifier, null, out byte b))
        {
            result = new Color(r, g, b);
            return true;
        }

        if (TryConvertAnsiColorToConsoleColor(input, out var consoleColor))
        {
            result = Color.FromConsoleColor(consoleColor);
            return true;
        }

        result = default;
        return false;
    }

    private static bool TryConvertAnsiColorToConsoleColor(string ansiColorName, out ConsoleColor result)
    {
        switch (ansiColorName)
        {
            case "Black":
                result = ConsoleColor.Black;
                return true;

            case "Red":
                result = ConsoleColor.DarkRed;
                return true;

            case "Green":
                result = ConsoleColor.DarkGreen;
                return true;

            case "Yellow":
                result = ConsoleColor.DarkYellow;
                return true;

            case "Blue":
                result = ConsoleColor.DarkBlue;
                return true;

            case "Magenta":
                result = ConsoleColor.DarkMagenta;
                return true;

            case "Cyan":
                result = ConsoleColor.DarkCyan;
                return true;

            case "White":
                result = ConsoleColor.Gray;
                return true;

            case "BrightBlack":
                result = ConsoleColor.DarkGray;
                return true;

            case "BrightRed":
                result = ConsoleColor.Red;
                return true;

            case "BrightGreen":
                result = ConsoleColor.Green;
                return true;

            case "BrightYellow":
                result = ConsoleColor.Yellow;
                return true;

            case "BrightBlue":
                result = ConsoleColor.Blue;
                return true;

            case "BrightMagenta":
                result = ConsoleColor.Magenta;
                return true;

            case "BrightCyan":
                result = ConsoleColor.Cyan;
                return true;

            case "BrightWhite":
                result = ConsoleColor.White;
                return true;

            default:
                result = default;
                return false;
        };
    }
}