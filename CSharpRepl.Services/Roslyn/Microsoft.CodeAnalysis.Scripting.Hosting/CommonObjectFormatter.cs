// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Globalization;
using CSharpRepl.Services;
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

    protected virtual MemberFilter Filter { get; } = new CommonMemberFilter();

    protected abstract CommonTypeNameFormatter TypeNameFormatter { get; }
    protected abstract CommonPrimitiveFormatter PrimitiveFormatter { get; }

    public StyledStringSegment NullLiteral => PrimitiveFormatter.NullLiteral;

    protected CommonObjectFormatter(SyntaxHighlighter syntaxHighlighter, Configuration config)
    {
        this.highlighter = syntaxHighlighter;
        this.configuration = config;
    }

    public StyledString FormatObject(object obj, PrintOptions options)
    {
        if (options == null)
        {
            // We could easily recover by using default options, but it makes
            // more sense for the host to choose the defaults so we'll require
            // that options be passed.
            throw new ArgumentNullException(nameof(options));
        }

        var visitor = new Visitor(this, TypeNameFormatter, GetInternalBuilderOptions(options), GetPrimitiveOptions(options), GetTypeNameOptions(options), options.MemberDisplayFormat, highlighter, configuration);
        return visitor.FormatObject(obj, level: options.MemberDisplayFormat == MemberDisplayFormat.SeparateLines ? Level.FirstDetailed : Level.FirstSimple);
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

    protected virtual CommonTypeNameFormatterOptions GetTypeNameOptions(PrintOptions printOptions) =>
        new(
            arrayBoundRadix: printOptions.NumberRadix,
            showNamespaces: false);
}