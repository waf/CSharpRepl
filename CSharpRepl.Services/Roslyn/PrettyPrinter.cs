// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using CSharpRepl.Services.SyntaxHighlighting;
using CSharpRepl.Services.Theming;
using Microsoft.CodeAnalysis.CSharp.Scripting.Hosting;
using Microsoft.CodeAnalysis.Scripting;
using Microsoft.CodeAnalysis.Scripting.Hosting;
using Spectre.Console;

namespace CSharpRepl.Services.Roslyn;

internal sealed class PrettyPrinter
{
    private readonly CSharpObjectFormatterImpl formatter;
    private readonly PrintOptions summaryOptions;
    private readonly PrintOptions detailedOptions;

    public PrettyPrinter(SyntaxHighlighter syntaxHighlighter, Configuration config)
    {
        formatter = new CSharpObjectFormatterImpl(syntaxHighlighter, config);
        summaryOptions = new PrintOptions
        {
            MemberDisplayFormat = MemberDisplayFormat.SingleLine,
            MaximumOutputLength = 20_000,
        };
        detailedOptions = new PrintOptions
        {
            MemberDisplayFormat = MemberDisplayFormat.SeparateLines,
            MaximumOutputLength = 20_000,
        };
    }

    public StyledString FormatObject(object? obj, bool displayDetails)
    {
        return obj switch
        {
            null => formatter.NullLiteral,

            // when displayDetails is true, don't show the escaped string (i.e. interpret the escape characters, via displaying to console)
            string str when displayDetails => str,

            //call stack for compilation error exception is useless
            CompilationErrorException compilationErrorException => compilationErrorException.Message,

            Exception exception =>
                    displayDetails ?
                    formatter.FormatException(exception) :
                    new StyledStringSegment(exception.Message, new Style(foreground: Color.Red)),

            _ => FormatObjectSafe(obj, displayDetails ? detailedOptions : summaryOptions)
        };
    }

    private StyledString FormatObjectSafe(object obj, PrintOptions options)
    {
        try
        {
            return formatter.FormatObject(obj, options);
        }
        catch (Exception) // sometimes the roslyn formatter APIs fail to format. Most notably with ref structs.
        {
            return obj.ToString() ?? "";
        }
    }
}