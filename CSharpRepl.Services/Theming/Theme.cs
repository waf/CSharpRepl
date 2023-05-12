// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;
using Microsoft.CodeAnalysis.Classification;
using PrettyPrompt.Highlighting;
using Spectre.Console;

namespace CSharpRepl.Services.Theming;

public sealed class Theme
{
    private static readonly Lazy<Theme> defaultTheme = new(
        () =>
        new(
            selectedCompletionItemBackground: null,
            completionBoxBorderColor: null,
            completionItemDescriptionPaneBackground: null,
            selectedTextBackground: null,
            syntaxHighlightingColors: new[]
            {
                new SyntaxHighlightingColor(name: ClassificationTypeNames.ClassName, foreground: "BrightCyan"),
                new SyntaxHighlightingColor(name: ClassificationTypeNames.StructName, foreground: "BrightCyan"),
                new SyntaxHighlightingColor(name: ClassificationTypeNames.DelegateName, foreground: "BrightCyan"),
                new SyntaxHighlightingColor(name: ClassificationTypeNames.InterfaceName, foreground: "BrightCyan"),
                new SyntaxHighlightingColor(name: ClassificationTypeNames.ModuleName, foreground: "BrightCyan"),
                new SyntaxHighlightingColor(name: ClassificationTypeNames.RecordClassName, foreground: "BrightCyan"),
                new SyntaxHighlightingColor(name: "record struct name", foreground: "BrightCyan"),
                new SyntaxHighlightingColor(name: ClassificationTypeNames.EnumName, foreground: "Green"),
                new SyntaxHighlightingColor(name: ClassificationTypeNames.Text, foreground: "White"),
                new SyntaxHighlightingColor(name: ClassificationTypeNames.ConstantName, foreground: "White"),
                new SyntaxHighlightingColor(name: ClassificationTypeNames.EnumMemberName, foreground: "White"),
                new SyntaxHighlightingColor(name: ClassificationTypeNames.EventName, foreground: "White"),
                new SyntaxHighlightingColor(name: ClassificationTypeNames.ExtensionMethodName, foreground: "White"),
                new SyntaxHighlightingColor(name: ClassificationTypeNames.Identifier, foreground: "White"),
                new SyntaxHighlightingColor(name: ClassificationTypeNames.LabelName, foreground: "White"),
                new SyntaxHighlightingColor(name: ClassificationTypeNames.LocalName, foreground: "White"),
                new SyntaxHighlightingColor(name: ClassificationTypeNames.MethodName, foreground: "White"),
                new SyntaxHighlightingColor(name: ClassificationTypeNames.PropertyName, foreground: "White"),
                new SyntaxHighlightingColor(name: ClassificationTypeNames.NamespaceName, foreground: "White"),
                new SyntaxHighlightingColor(name: ClassificationTypeNames.ParameterName, foreground: "White"),
                new SyntaxHighlightingColor(name: ClassificationTypeNames.NumericLiteral, foreground: "Blue"),
                new SyntaxHighlightingColor(name: ClassificationTypeNames.ControlKeyword, foreground: "BrightMagenta"),
                new SyntaxHighlightingColor(name: ClassificationTypeNames.Keyword, foreground: "BrightMagenta"),
                new SyntaxHighlightingColor(name: ClassificationTypeNames.Operator, foreground: "BrightMagenta"),
                new SyntaxHighlightingColor(name: ClassificationTypeNames.OperatorOverloaded, foreground: "BrightMagenta"),
                new SyntaxHighlightingColor(name: ClassificationTypeNames.PreprocessorKeyword, foreground: "BrightMagenta"),
                new SyntaxHighlightingColor(name: ClassificationTypeNames.StringEscapeCharacter, foreground: "BrightMagenta"),
                new SyntaxHighlightingColor(name: ClassificationTypeNames.VerbatimStringLiteral, foreground: "BrightYellow"),
                new SyntaxHighlightingColor(name: ClassificationTypeNames.StringLiteral, foreground: "BrightYellow"),
                new SyntaxHighlightingColor(name: ClassificationTypeNames.TypeParameterName, foreground: "Yellow"),
                new SyntaxHighlightingColor(name: ClassificationTypeNames.Comment, foreground: "Cyan"),
                new SyntaxHighlightingColor(name: ClassificationTypeNames.XmlDocCommentAttributeQuotes, foreground: "Green"),
                new SyntaxHighlightingColor(name: ClassificationTypeNames.XmlDocCommentAttributeValue, foreground: "Green"),
                new SyntaxHighlightingColor(name: ClassificationTypeNames.XmlDocCommentAttributeName, foreground: "Cyan"),
                new SyntaxHighlightingColor(name: ClassificationTypeNames.XmlDocCommentCDataSection, foreground: "Cyan"),
                new SyntaxHighlightingColor(name: ClassificationTypeNames.XmlDocCommentComment, foreground: "Cyan"),
                new SyntaxHighlightingColor(name: ClassificationTypeNames.XmlDocCommentDelimiter, foreground: "Cyan"),
                new SyntaxHighlightingColor(name: ClassificationTypeNames.XmlDocCommentEntityReference, foreground: "Cyan"),
                new SyntaxHighlightingColor(name: ClassificationTypeNames.XmlDocCommentName, foreground: "Cyan"),
                new SyntaxHighlightingColor(name: ClassificationTypeNames.XmlDocCommentProcessingInstruction, foreground: "Cyan"),
                new SyntaxHighlightingColor(name: ClassificationTypeNames.XmlDocCommentText, foreground: "Cyan")
            }));

    public static Theme DefaultTheme => defaultTheme.Value;

    public string? SelectedCompletionItemBackground { get; }
    public AnsiColor? GetSelectedCompletionItemBackgroundColor()
        => SelectedCompletionItemBackground is null ? null : new ThemeColor(SelectedCompletionItemBackground).ToAnsiColor();

    public string? CompletionBoxBorderColor { get; }
    public ConsoleFormat? GetCompletionBoxBorderFormat()
        => CompletionBoxBorderColor is null ? null : new ConsoleFormat(Foreground: new ThemeColor(CompletionBoxBorderColor).ToAnsiColor());

    public string? CompletionItemDescriptionPaneBackground { get; }
    public AnsiColor? GetCompletionItemDescriptionPaneBackground()
        => CompletionItemDescriptionPaneBackground is null ? null : new ThemeColor(CompletionItemDescriptionPaneBackground).ToAnsiColor();

    public string? SelectedTextBackground { get; }
    public AnsiColor? GetSelectedTextBackground()
        => SelectedTextBackground is null ? null : new ThemeColor(SelectedTextBackground).ToAnsiColor();

    public SyntaxHighlightingColor[] SyntaxHighlightingColors { get; }

    [JsonIgnore]
    private readonly Dictionary<string, AnsiColor> syntaxHighlightingAnsiColorsDictionary;

    [JsonIgnore]
    private readonly Dictionary<string, Color> syntaxHighlightingSpectreColorsDictionary;

    public Theme(
        string? selectedCompletionItemBackground,
        string? completionBoxBorderColor,
        string? completionItemDescriptionPaneBackground,
        string? selectedTextBackground,
        SyntaxHighlightingColor[] syntaxHighlightingColors)
    {
        SelectedCompletionItemBackground = selectedCompletionItemBackground;
        CompletionBoxBorderColor = completionBoxBorderColor;
        CompletionItemDescriptionPaneBackground = completionItemDescriptionPaneBackground;
        SelectedTextBackground = selectedTextBackground;

        SyntaxHighlightingColors = syntaxHighlightingColors;
        syntaxHighlightingAnsiColorsDictionary = syntaxHighlightingColors.ToDictionary(c => c.Name, c => new ThemeColor(c.Foreground).ToAnsiColor());
        syntaxHighlightingSpectreColorsDictionary = syntaxHighlightingColors.ToDictionary(c => c.Name, c => new ThemeColor(c.Foreground).ToSpectreColor());
    }

    public AnsiColor? GetSyntaxHighlightingAnsiColor(string? classification)
        => TryGetSyntaxHighlightingAnsiColor(classification, out var color) ? color : null;

    public AnsiColor GetSyntaxHighlightingAnsiColor(string? classification, AnsiColor defaultValue)
        => classification is null ? default : syntaxHighlightingAnsiColorsDictionary.GetValueOrDefault(classification, defaultValue);

    public bool TryGetSyntaxHighlightingAnsiColor(string? classification, out AnsiColor color)
    {
        if (classification is null)
        {
            color = default;
            return false;
        }
        return syntaxHighlightingAnsiColorsDictionary.TryGetValue(classification, out color);
    }

    public Color? GetSyntaxHighlightingSpectreColor(string? classification)
        => TryGetSyntaxHighlightingSpectreColor(classification, out var color) ? color : null;

    public Color GetSyntaxHighlightingSpectreColor(string? classification, Color defaultValue)
        => classification is null ? default : syntaxHighlightingSpectreColorsDictionary.GetValueOrDefault(classification, defaultValue);

    public bool TryGetSyntaxHighlightingSpectreColor(string? classification, out Color color)
    {
        if (classification is null)
        {
            color = default;
            return false;
        }
        return syntaxHighlightingSpectreColorsDictionary.TryGetValue(classification, out color);
    }
}