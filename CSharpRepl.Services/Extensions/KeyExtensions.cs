// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Linq;
using PrettyPrompt.Consoles;

namespace CSharpRepl.Services.Extensions;

public static class KeyExtensions
{
    public static string GetStringValue(this KeyPressPattern pattern)
    {
        if (pattern.Key != default)
        {
            return pattern.Modifiers == default
                ? $"{pattern.Key}"
                : $"{pattern.Modifiers.GetStringValue()}+{pattern.Key}";
        }
        return $"{pattern.Character}";
    }

    private static string GetStringValue(this ConsoleModifiers modifiers)
    {
        var values = new[] { ConsoleModifiers.Control, ConsoleModifiers.Alt, ConsoleModifiers.Shift }
            .Where(x => modifiers.HasFlag(x))
            .OrderDescending()
            .Select(x => x.ToString());
        return string.Join("+", values);
    }
}