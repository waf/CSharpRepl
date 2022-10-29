// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using Microsoft.CodeAnalysis.CSharp.Scripting.Hosting;
using Microsoft.CodeAnalysis.Scripting.Hosting;
using PrettyPrompt.Highlighting;

namespace CSharpRepl.Services.Roslyn;

internal sealed class PrettyPrinter
{
    public static readonly PrettyPrinter Instance = new();

    private static readonly CSharpObjectFormatterImpl formatter = new();

    private readonly PrintOptions summaryOptions;
    private readonly PrintOptions detailedOptions;

    private PrettyPrinter()
    {
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

    public FormattedString FormatObject(object? obj, bool displayDetails)
    {
        return obj switch
        {
            // intercept null, don't print the string "null"
            null => null,

            // when displayDetails is true, don't show the escaped string (i.e. interpret the escape characters, via displaying to console)
            string str when displayDetails => str,

            Exception exception =>
                    displayDetails ?
                    formatter.FormatException(exception) :
                    new FormattedString(exception.Message, new ConsoleFormat(AnsiColor.Red)),

            _ => FormatObjectSafe(obj, displayDetails ? detailedOptions : summaryOptions)
        };
    }

    private FormattedString FormatObjectSafe(object obj, PrintOptions options)
    {
        try
        {
            return formatter.FormatObject(obj, options);
        }
        catch (Exception) // sometimes the roslyn formatter APIs fail to format. Most notably with ref structs.
        {
            return obj.ToString();
        }
    }
}