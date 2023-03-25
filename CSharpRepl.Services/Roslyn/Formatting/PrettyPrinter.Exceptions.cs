// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using CSharpRepl.Services.SyntaxHighlighting;
using CSharpRepl.Services.Theming;
using Microsoft.CodeAnalysis.Classification;

namespace CSharpRepl.Services.Roslyn.Formatting;

/// <summary>
/// Object pretty printer.
/// </summary>
internal sealed partial class PrettyPrinter
{
    public StyledString FormatException(Exception exception, Level level)
    {
        if (level != Level.FirstDetailed) return exception.Message;

        var builder = new StyledStringBuilder();

        ExceptionFormatter.AppendType(this, builder, exception.GetType(), fullName: true);
        builder.Append(": ");
        builder.Append(exception.Message);

        var frames = (EnhancedStackFrame[])new EnhancedStackTrace(exception).GetFrames();
        var count = frames.Length;
        const int ScriptRunnerMethods = 5;
        for (var i = 0; i < count - ScriptRunnerMethods; i++)
        {
            builder.Append(Environment.NewLine);
            builder.Append(" ");

            var frame = frames[i];
            builder.Append("   at ");
            ExceptionFormatter.AppendMethod(this, frame.MethodInfo, builder, syntaxHighlighter);

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
}

file class ExceptionFormatter
{
    public static StyledStringBuilder AppendType(
        PrettyPrinter prettyPrinter,
        StyledStringBuilder builder,
        Type? type,
        bool fullName = true,
        IList<string>? tupleNames = null)
    {
        if (type is null) return builder;

        builder.Append(prettyPrinter.FormatTypeName(type, showNamespaces: fullName, useLanguageKeywords: true, hideSystemNamespace: true, tupleNames));

        return builder;
    }

    //Modified version of https://github.com/benaadams/Ben.Demystifier/blob/main/src/Ben.Demystifier/ResolvedMethod.cs
    public static StyledStringBuilder AppendMethod(PrettyPrinter prettyPrinter, ResolvedMethod method, StyledStringBuilder builder, SyntaxHighlighter highlighter)
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

                AppendType(prettyPrinter, builder, method.DeclaringType);
            }
            else if (method.Name == ".cctor")
            {
                builder.Append("static ", highlighter.KeywordStyle);
                AppendType(prettyPrinter, builder, method.DeclaringType);
            }
            else
            {
                var builderLengthBefore = builder.Length;
                AppendType(prettyPrinter, builder, method.DeclaringType);
                if (builderLengthBefore < builder.Length) builder.Append(".");

                if (method.Name != null)
                {
                    builder.Append(method.Name, highlighter.GetStyle(ClassificationTypeNames.MethodName));
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
                if (parameter.ResolvedType == typeof(void))
                {
                    sb.Append("void", highlighter.KeywordStyle);
                }
                else
                {
                    IList<string>? tupleNames = null;
                    if (parameter is ValueTupleResolvedParameter { TupleNames: { } tupleNames2 }) { tupleNames = tupleNames2; };
                    AppendType(prettyPrinter, builder, parameter.ResolvedType, fullName: false, tupleNames: tupleNames);
                }
            }
            else
            {
                sb.Append("?");
            }

            if (!string.IsNullOrEmpty(parameter.Name))
            {
                sb.Append(" ")
                  .Append(parameter.Name, highlighter.GetStyle(ClassificationTypeNames.ParameterName));
            }

            return sb;
        }
    }
}