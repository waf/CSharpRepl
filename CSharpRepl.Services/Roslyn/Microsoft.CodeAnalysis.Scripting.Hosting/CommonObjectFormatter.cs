// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using CSharpRepl.Services;
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

    public virtual CommonMemberFilter Filter { get; } = new CommonMemberFilter();

    public abstract CommonTypeNameFormatter TypeNameFormatter { get; }
    public abstract CommonPrimitiveFormatter PrimitiveFormatter { get; }

    public StyledStringSegment NullLiteral => PrimitiveFormatter.NullLiteral;

    protected CommonObjectFormatter(SyntaxHighlighter syntaxHighlighter, Configuration config)
    {
        highlighter = syntaxHighlighter;
        configuration = config;
    }

    public virtual CommonTypeNameFormatterOptions GetTypeNameOptions(PrintOptions printOptions) =>
        new(
            arrayBoundRadix: printOptions.NumberRadix,
            showNamespaces: false);
}