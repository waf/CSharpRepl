// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Globalization;
using CSharpRepl.Services;
using CSharpRepl.Services.Roslyn;
using CSharpRepl.Services.Roslyn.CustomObjectFormatters;
using CSharpRepl.Services.SyntaxHighlighting;
using CSharpRepl.Services.Theming;

namespace Microsoft.CodeAnalysis.Scripting.Hosting;

/// <summary>
/// Object pretty printer.
/// </summary>
internal abstract partial class CommonObjectFormatter
{
    protected readonly SyntaxHighlighter highlighter;
    protected readonly Configuration configuration;

    public virtual MemberFilter Filter { get; } = new CommonMemberFilter();

    public abstract CommonTypeNameFormatter TypeNameFormatter { get; }
    public abstract CommonPrimitiveFormatter PrimitiveFormatter { get; }

    public StyledStringSegment NullLiteral => PrimitiveFormatter.NullLiteral;

    protected CommonObjectFormatter(SyntaxHighlighter syntaxHighlighter, Configuration config)
    {
        highlighter = syntaxHighlighter;
        configuration = config;
    }

    protected virtual BuilderOptions GetInternalBuilderOptions(PrintOptions printOptions) =>
        new(
            indentation: "  ",
            newLine: Environment.NewLine,
            ellipsis: printOptions.Ellipsis,
            maximumLineLength: int.MaxValue,
            maximumOutputLength: printOptions.MaximumOutputLength);

    protected virtual CommonPrimitiveFormatterOptions GetPrimitiveOptions(PrintOptions printOptions) =>
        new(
            numberRadix: printOptions.NumberRadix,
            includeCodePoints: false,
            quoteStringsAndCharacters: true,
            escapeNonPrintableCharacters: printOptions.EscapeNonPrintableCharacters,
            cultureInfo: CultureInfo.CurrentUICulture);

    public virtual CommonTypeNameFormatterOptions GetTypeNameOptions(PrintOptions printOptions) =>
        new(
            arrayBoundRadix: printOptions.NumberRadix,
            showNamespaces: false);
}