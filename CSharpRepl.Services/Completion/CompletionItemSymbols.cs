// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using CSharpRepl.Services.Theming;
using Microsoft.CodeAnalysis.Classification;
using PrettyPrompt;
using PrettyPrompt.Highlighting;
using Spectre.Console;

namespace CSharpRepl.Services.Completion;

/// <summary>
/// The glyph shown before a completion/member name and the terminal-palette color it should be
/// drawn in (<see langword="null"/> = default/uncolored).
/// </summary>
public readonly record struct CompletionItemSymbol(string Prefix, int GlyphLength, ConsoleColor? Color);

/// <summary>
/// Maps a Roslyn classification to the leading glyph (and its color) shown for that kind.
/// </summary>
public static class CompletionItemSymbols
{
    public static CompletionItemSymbol Get(string? classification, bool useUnicode)
    {
        if (!useUnicode)
            return new CompletionItemSymbol("", 0, null);

        // Mnemonic circled letters (the kind's initial) so it's more clear what is a method, property, field, etc
        (string Glyph, ConsoleColor? Color) symbol = classification switch
        {
            ClassificationTypeNames.Keyword => ("Ⓚ", null),
            ClassificationTypeNames.MethodName or ClassificationTypeNames.ExtensionMethodName => ("Ⓜ", ConsoleColor.DarkBlue),
            ClassificationTypeNames.PropertyName => ("Ⓟ", null),
            ClassificationTypeNames.FieldName or ClassificationTypeNames.ConstantName or ClassificationTypeNames.EnumMemberName => ("Ⓕ", ConsoleColor.Cyan),
            ClassificationTypeNames.EventName => ("↯", ConsoleColor.Yellow),
            ClassificationTypeNames.ClassName or ClassificationTypeNames.RecordClassName => ("Ⓒ", null),
            ClassificationTypeNames.InterfaceName => ("Ⓘ", null),
            ClassificationTypeNames.StructName or ClassificationTypeNames.RecordStructName => ("Ⓢ", null),
            ClassificationTypeNames.EnumName => ("Ⓔ", null),
            ClassificationTypeNames.DelegateName => ("Ⓓ", null),
            ClassificationTypeNames.NamespaceName => ("Ⓝ", null),
            ClassificationTypeNames.TypeParameterName => ("Ⓣ", null),
            _ => ("•", null),
        };

        // glyph + two padding spaces for clear separation from the name.
        return new CompletionItemSymbol(symbol.Glyph + "  ", symbol.Glyph.Length, symbol.Color);
    }

    public static AnsiColor? GetIconAnsiColor(ConsoleColor? color)
        => color is not null && !PromptConfiguration.HasUserOptedOutFromColor ? FormattedStringParser.FromConsoleColor(color.Value) : null;

    public static Color? GetIconSpectreColor(ConsoleColor? color)
        => color is not null && !PromptConfiguration.HasUserOptedOutFromColor ? Color.FromConsoleColor(color.Value) : null;
}
