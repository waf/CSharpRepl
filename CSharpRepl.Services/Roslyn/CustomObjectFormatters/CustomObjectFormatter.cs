// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using CSharpRepl.Services.SyntaxHighlighting;
using CSharpRepl.Services.Theming;
using Spectre.Console;

namespace CSharpRepl.Services.Roslyn.CustomObjectFormatters;

internal interface ICustomObjectFormatter
{
    /// <summary>
    /// Is all the information about the value present in the formatted result?
    /// When true the members of value are not showed even when requesting detailed output.
    /// </summary>
    bool IsFormattingExhaustive { get; }

    /// <summary>
    /// Is this formatter to the value?
    /// </summary>
    bool IsApplicable(object? value);

    StyledString FormatToText(object? value, Level level, Formatter formatter);

    //TODO - Hubert
    //IRenderable FormatToRenderable(object? value, Level level, Formatter formatter);
}

internal abstract class CustomObjectFormatter : ICustomObjectFormatter
{
    public bool IsApplicable(object? value)
    {
        if (value is null) return true;
        return value.GetType().IsAssignableTo(Type);
    }

    public abstract Type Type { get; }

    //TODO - Hubert - not necessary anymore?
    public abstract bool IsFormattingExhaustive { get; }

    StyledString ICustomObjectFormatter.FormatToText(object? value, Level level, Formatter formatter)
    {
        if (value is null) return formatter.NullLiteral;
        return Format(value, level, formatter);
    }

    protected abstract StyledString Format(object value, Level level, Formatter formatter);
}

internal abstract class CustomObjectFormatter<T> : CustomObjectFormatter
    where T : notnull
{
    public sealed override Type Type => typeof(T);

    protected sealed override StyledString Format(object value, Level level, Formatter formatter)
        => Format((T)value, level, formatter);

    public abstract StyledString Format(T value, Level level, Formatter formatter);
}

internal class Formatter
{
    private readonly PrettyPrinter prettyPrinter;
    private readonly SyntaxHighlighter syntaxHighlighter;

    public StyledStringSegment NullLiteral => prettyPrinter.NullLiteral;
    public Style KeywordStyle => syntaxHighlighter.KeywordStyle;

    public Formatter(PrettyPrinter prettyPrinter, SyntaxHighlighter syntaxHighlighter)
    {
        this.prettyPrinter = prettyPrinter;
        this.syntaxHighlighter = syntaxHighlighter;
    }

    public StyledString FormatObjectToText(object? obj, Level level)
        => prettyPrinter.FormatObjectSafeToStyledString(obj, level, quoteStringsAndCharacters: null);

    public StyledString FormatTypeName(Type type, bool showNamespaces, bool useLanguageKeywords)
        => prettyPrinter.FormatTypeName(type, showNamespaces, useLanguageKeywords);

    public Style GetStyle(string? classification)
        => syntaxHighlighter.GetStyle(classification);
}