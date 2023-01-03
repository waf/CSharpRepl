// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using CSharpRepl.Services;
using CSharpRepl.Services.SyntaxHighlighting;
using CSharpRepl.Services.Theming;
using Microsoft.CodeAnalysis.Classification;
using Spectre.Console;

namespace Microsoft.CodeAnalysis.Scripting.Hosting;

/// <summary>
/// Object pretty printer.
/// </summary>
internal abstract partial class CommonObjectFormatter
{
    private readonly SyntaxHighlighter highlighter;
    private readonly Configuration config;

    protected CommonObjectFormatter(SyntaxHighlighter syntaxHighlighter, Configuration config)
    {
        this.highlighter = syntaxHighlighter;
        this.config = config;
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

        var visitor = new Visitor(this, TypeNameFormatter, GetInternalBuilderOptions(options), GetPrimitiveOptions(options), GetTypeNameOptions(options), options.MemberDisplayFormat, highlighter, config);
        return visitor.FormatObject(obj);
    }

    protected virtual MemberFilter Filter { get; } = new CommonMemberFilter();

    protected abstract CommonTypeNameFormatter TypeNameFormatter { get; }
    protected abstract CommonPrimitiveFormatter PrimitiveFormatter { get; }

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

    public StyledString FormatException(Exception e)
    {
        if (e == null)
        {
            throw new ArgumentNullException(nameof(e));
        }

        var builder = new StyledStringBuilder();

        builder.AppendType(e.GetType(), highlighter, fullName: true);
        builder.Append(": ");
        builder.Append(e.Message);

        var frames = (EnhancedStackFrame[])new EnhancedStackTrace(e).GetFrames();
        var count = frames.Length;
        const int ScriptRunnerMethods = 5;
        for (var i = 0; i < count - ScriptRunnerMethods; i++)
        {
            builder.Append(Environment.NewLine);
            builder.Append(" ");

            var frame = frames[i];
            builder.Append("   at ");
            AppendMethod(frame.MethodInfo, builder);

            if (frame.GetFileName() is { Length: > 0 } fileName)
            {
                builder.Append(" in ");
                builder.Append(EnhancedStackTrace.TryGetFullPath(fileName));
            }

            var lineNo = frame.GetFileLineNumber();
            if (lineNo != 0)
            {
                builder.Append($":line {lineNo}");
            }
        }

        return builder.ToStyledString();
    }

    //Modified version of https://github.com/benaadams/Ben.Demystifier/blob/main/src/Ben.Demystifier/ResolvedMethod.cs
    private StyledStringBuilder AppendMethod(ResolvedMethod method, StyledStringBuilder builder)
    {
        if (method.IsAsync)
        {
            builder.Append("async ", highlighter.KeywordStyle);
        }

        if (method.ReturnParameter != null)
        {
            AppendParameter(method.ReturnParameter, builder).Append(" ");
        }

        if (method.DeclaringType != null)
        {
            if (method.Name == ".ctor")
            {
                if (string.IsNullOrEmpty(method.SubMethod) && !method.IsLambda)
                    builder.Append("new ", highlighter.KeywordStyle);

                builder.AppendType(method.DeclaringType, highlighter);
            }
            else if (method.Name == ".cctor")
            {
                builder.Append("static ", highlighter.KeywordStyle);
                builder.AppendType(method.DeclaringType, highlighter);
            }
            else
            {
                var builderLengthBefore = builder.Length;
                builder.AppendType(method.DeclaringType, highlighter);
                if (builderLengthBefore < builder.Length) builder.Append(".");

                if (method.Name != null)
                {
                    builder.Append(method.Name, new Style(foreground: highlighter.GetSpectreColor(ClassificationTypeNames.MethodName)));
                }
            }
        }
        else
        {
            builder.Append(method.Name);
        }
        builder.Append(method.GenericArguments);

        builder.Append("(");
        if (method.MethodBase != null)
        {
            var isFirst = true;
            foreach (var param in method.Parameters)
            {
                if (isFirst)
                {
                    isFirst = false;
                }
                else
                {
                    builder.Append(", ");
                }
                AppendParameter(param, builder);
            }
        }
        else
        {
            builder.Append("?");
        }
        builder.Append(")");

        if (!string.IsNullOrEmpty(method.SubMethod) || method.IsLambda)
        {
            builder.Append("+");
            builder.Append(method.SubMethod);
            builder.Append("(");
            if (method.SubMethodBase != null)
            {
                var isFirst = true;
                foreach (var param in method.SubMethodParameters)
                {
                    if (isFirst)
                    {
                        isFirst = false;
                    }
                    else
                    {
                        builder.Append(", ");
                    }
                    AppendParameter(param, builder);
                }
            }
            else
            {
                builder.Append("?");
            }
            builder.Append(")");
            if (method.IsLambda)
            {
                builder.Append(" => { }");

                if (method.Ordinal.HasValue)
                {
                    builder.Append(" [");
                    builder.Append(method.Ordinal?.ToString());
                    builder.Append("]");
                }
            }
        }

        if (method.RecurseCount > 0)
        {
            builder.Append($" x {method.RecurseCount + 1:0}");
        }

        return builder;

        //https://github.com/benaadams/Ben.Demystifier/blob/main/src/Ben.Demystifier/ResolvedParameter.cs
        StyledStringBuilder AppendParameter(ResolvedParameter parameter, StyledStringBuilder sb)
        {
            if (parameter.ResolvedType.Assembly.ManifestModule.Name == "FSharp.Core.dll" && parameter.ResolvedType.Name == "Unit")
                return sb;

            if (!string.IsNullOrEmpty(parameter.Prefix))
            {
                sb.Append(parameter.Prefix)
                  .Append(" ");
            }

            if (parameter.IsDynamicType)
            {
                sb.Append("dynamic", highlighter.KeywordStyle);
            }
            else if (parameter.ResolvedType != null)
            {
                IList<string>? tupleNames = null;
                if (parameter is ValueTupleResolvedParameter { TupleNames: { } tupleNames2 }) { tupleNames = tupleNames2; };
                builder.AppendType(parameter.ResolvedType, highlighter, fullName: false, tupleNames);
            }
            else
            {
                sb.Append("?");
            }

            if (!string.IsNullOrEmpty(parameter.Name))
            {
                sb.Append(" ")
                  .Append(parameter.Name, new Style(foreground: highlighter.GetSpectreColor(ClassificationTypeNames.ParameterName)));
            }

            return sb;
        }
    }
}