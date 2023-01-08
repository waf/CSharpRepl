// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using CSharpRepl.Services.Theming;
using Microsoft.CodeAnalysis.Scripting.Hosting;
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

    StyledString Format(object? value, Level level, Formatter formatter);
}

internal abstract class CustomObjectFormatter : ICustomObjectFormatter
{
    public bool IsApplicable(object? value)
    {
        if (value is null) return true;
        return value.GetType().IsAssignableTo(Type);
    }

    public abstract Type Type { get; }
    public abstract bool IsFormattingExhaustive { get; }

    StyledString ICustomObjectFormatter.Format(object? value, Level level, Formatter formatter)
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
    private readonly CommonObjectFormatter.Visitor visitor;

    public StyledStringSegment NullLiteral => visitor.Formatter.NullLiteral;
    public Style KeywordStyle => visitor.SyntaxHighlighter.KeywordStyle;

    public Formatter(CommonObjectFormatter.Visitor visitor)
    {
        this.visitor = visitor;
    }

    public StyledString FormatObject(object? obj, Level level)
        => visitor.FormatObject(obj, level);

    public StyledString FormatTypeName(Type type, bool showNamespaces, bool useLanguageKeywords)
        => visitor.TypeNameFormatter.FormatTypeName(
            type,
            new CommonTypeNameFormatterOptions(
                arrayBoundRadix: visitor.TypeNameOptions.ArrayBoundRadix,
                showNamespaces,
                useLanguageKeywords));

    public Style GetStyle(string? classification)
        => visitor.SyntaxHighlighter.GetStyle(classification);
}

internal enum Level
{
    FirstDetailed,
    FirstSimple,
    Second,
    ThirdPlus
}

internal static class LevelX
{
    public static Level Increment(this Level level) => (Level)Math.Min((int)level + 1, (int)Level.ThirdPlus);
}