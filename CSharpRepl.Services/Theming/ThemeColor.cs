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
}