// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using CSharpRepl.Services;
using CSharpRepl.Services.SyntaxHighlighting;
using Microsoft.CodeAnalysis.Scripting.Hosting;
using MemberFilter = Microsoft.CodeAnalysis.Scripting.Hosting.MemberFilter;

namespace Microsoft.CodeAnalysis.CSharp.Scripting.Hosting;

internal class CSharpObjectFormatterImpl : CommonObjectFormatter
{
    protected override CommonTypeNameFormatter TypeNameFormatter { get; }
    protected override CommonPrimitiveFormatter PrimitiveFormatter { get; }
    protected override MemberFilter Filter { get; }

    internal CSharpObjectFormatterImpl(SyntaxHighlighter syntaxHighlighter, Configuration config)
        : base(syntaxHighlighter, config)
    {
        PrimitiveFormatter = new CSharpPrimitiveFormatter(syntaxHighlighter);
        TypeNameFormatter = new CSharpTypeNameFormatter(PrimitiveFormatter, syntaxHighlighter);
        Filter = new CSharpMemberFilter();
    }
}