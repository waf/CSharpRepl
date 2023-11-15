using System;
using System.Linq;
using PrettyPrompt.Consoles;

namespace CSharpRepl.Services.Extensions;

public static class KeyExtensions
{
    private static string GetStringValue(this KeyPressPattern pattern)
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

    public static string GetStringValue(this KeyPressPatterns patterns)
        => patterns.DefinedPatterns!.First().GetStringValue();
}