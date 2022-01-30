// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text.Json.Serialization;
using PrettyPrompt.Highlighting;

namespace CSharpRepl.Services.Theming;

internal readonly struct Color
{
    private static readonly Dictionary<string, AnsiColor> ansiColorNames =
        typeof(AnsiColor)
        .GetFields(BindingFlags.Static | BindingFlags.Public)
        .Where(f => f.FieldType == typeof(AnsiColor))
        .ToDictionary(f => f.Name, f => (AnsiColor)f.GetValue(null)!);

    [JsonConstructor]
    public Color(string name, string foreground)
    {
        Debug.Assert(!string.IsNullOrEmpty(name));
        Debug.Assert(!string.IsNullOrEmpty(foreground));

        Name = name;
        Foreground = foreground;
    }

    public string Name { get; }
    public string Foreground { get; }

    public AnsiColor ToAnsiColor()
    {
        var span = Foreground.AsSpan();
        if (Foreground.StartsWith('#') && Foreground.Length == 7 &&
            byte.TryParse(span.Slice(1, 2), NumberStyles.AllowHexSpecifier, null, out byte r) &&
            byte.TryParse(span.Slice(3, 2), NumberStyles.AllowHexSpecifier, null, out byte g) &&
            byte.TryParse(span.Slice(5, 2), NumberStyles.AllowHexSpecifier, null, out byte b))
        {
            return AnsiColor.RGB(r, g, b);
        }

        if (ansiColorNames.TryGetValue(Foreground, out AnsiColor? color))
        {
            return color;
        }

        throw new ArgumentException($"Unknown recognized color '{Foreground}'. Expecting either a hexadecimal color of the format #RRGGBB or a standard ANSI color name");
    }
}