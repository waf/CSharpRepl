// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Internal;
using System.Text.RegularExpressions;
using CSharpRepl.Services.SyntaxHighlighting;
using CSharpRepl.Services.Theming;
using Microsoft.CodeAnalysis.Classification;
using Microsoft.CodeAnalysis.Scripting.Hosting;

namespace CSharpRepl.Services.Roslyn;

/// <summary>
/// Object pretty printer.
/// </summary>
internal sealed partial class PrettyPrinter
{
    public StyledString FormatException(Exception exception, Level level)
    {
        if (level != Level.FirstDetailed) return exception.Message;

        var builder = new StyledStringBuilder();

        ExceptionFormatter.AppendType(builder, exception.GetType(), syntaxHighlighter, fullName: true);
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
            ExceptionFormatter.AppendMethod(frame.MethodInfo, builder, syntaxHighlighter);

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
        StyledStringBuilder builder,
        Type? type,
        SyntaxHighlighter highlighter,
        bool fullName = true,
        IList<string>? tupleNames = null)
    {
        if (type is null) return builder;

        if (TypeNameHelper.BuiltInTypeNames.TryGetValue(type, out var typeName))
        {
            builder.Append(typeName, highlighter.KeywordStyle);
            return builder;
        }

        TypeNameHelperInternal.AppendTypeDisplayName(builder, type, fullName, includeGenericParameterNames: true, tupleNames, highlighter);

        return builder;
    }

    //Modified version of https://github.com/benaadams/Ben.Demystifier/blob/main/src/Ben.Demystifier/ResolvedMethod.cs
    public static StyledStringBuilder AppendMethod(ResolvedMethod method, StyledStringBuilder builder, SyntaxHighlighter highlighter)
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

                AppendType(builder, method.DeclaringType, highlighter);
            }
            else if (method.Name == ".cctor")
            {
                builder.Append("static ", highlighter.KeywordStyle);
                AppendType(builder, method.DeclaringType, highlighter);
            }
            else
            {
                var builderLengthBefore = builder.Length;
                AppendType(builder, method.DeclaringType, highlighter);
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
                IList<string>? tupleNames = null;
                if (parameter is ValueTupleResolvedParameter { TupleNames: { } tupleNames2 }) { tupleNames = tupleNames2; };
                AppendType(builder, parameter.ResolvedType, highlighter, fullName: false, tupleNames);
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

    //Modified version of https://github.com/benaadams/Ben.Demystifier/blob/main/src/Ben.Demystifier/TypeNameHelper.cs
    private static class TypeNameHelperInternal
    {
        public static StyledStringBuilder AppendTypeDisplayName(
            StyledStringBuilder builder,
            Type type,
            bool fullName,
            bool includeGenericParameterNames,
            IList<string>? tupleNames,
            SyntaxHighlighter highlighter)
        {
            ProcessType(builder, type, new DisplayNameOptions(fullName, includeGenericParameterNames), tupleNames, highlighter);
            return builder;
        }

        private static void ProcessType(
            StyledStringBuilder builder,
            Type type,
            DisplayNameOptions options,
            IList<string>? tupleNames,
            SyntaxHighlighter highlighter)
        {
            if (type.IsValueTuple())
            {
                builder.Append('(');
                var itemTypes = type.GenericTypeArguments;
                for (int i = 0; i < itemTypes.Length; i++)
                {
                    ProcessType(builder, itemTypes[i], new(fullName: false, includeGenericParameterNames: true), tupleNames: null, highlighter);
                    if (tupleNames != null && itemTypes.Length == tupleNames.Count)
                    {
                        builder.Append(' ');
                        builder.Append(tupleNames[i]);
                    }
                    if (i + 1 == itemTypes.Length)
                    {
                        continue;
                    }
                    builder.Append(", ");
                }
                builder.Append(')');
            }
            else if (type.IsGenericType)
            {
                var underlyingType = Nullable.GetUnderlyingType(type);
                if (underlyingType != null)
                {
                    ProcessType(builder, underlyingType, options, tupleNames, highlighter);
                    builder.Append('?');
                }
                else
                {
                    var genericArguments = type.GetGenericArguments();
                    ProcessGenericType(builder, type, genericArguments, genericArguments.Length, options, highlighter);
                }
            }
            else if (type.IsArray)
            {
                ProcessArrayType(builder, type, options, tupleNames, highlighter);
            }
            else if (TypeNameHelper.BuiltInTypeNames.TryGetValue(type, out var builtInName))
            {
                builder.Append(builtInName, highlighter.KeywordStyle);
            }
            else
            {
                var format = TypeNameFormatter.GetTypeStyle(type, highlighter);
                if (type.Namespace == nameof(System))
                {
                    builder.Append(type.Name, format);
                }
                else if (
                    type.Assembly.ManifestModule.Name == "FSharp.Core.dll" &&
                    TypeNameHelper.FSharpTypeNames.TryGetValue(type.Name, out builtInName))
                {
                    builder.Append(builtInName, format);
                }
                else if (type.IsGenericParameter)
                {
                    if (options.IncludeGenericParameterNames)
                    {
                        builder.Append(type.Name, highlighter.GetStyle(ClassificationTypeNames.TypeParameterName));
                    }
                }
                else
                {
                    var name = options.FullName ? type.FullName ?? type.Name : type.Name;
                    if (!Regex.IsMatch(name, @"Submission#[0-9]+")) //https://github.com/waf/CSharpRepl/issues/194)
                    {
                        builder.Append(name, format);
                    }
                }
            }
        }

        private static void ProcessArrayType(
            StyledStringBuilder builder,
            Type type,
            DisplayNameOptions options,
            IList<string>? tupleNames,
            SyntaxHighlighter highlighter)
        {
            var innerType = type;
            while (innerType.IsArray)
            {
                if (innerType.GetElementType() is { } inner)
                {
                    innerType = inner;
                }
            }

            ProcessType(builder, innerType, options, tupleNames, highlighter);

            while (type.IsArray)
            {
                builder.Append('[');
                var commaCount = type.GetArrayRank() - 1;
                for (int i = 0; i < commaCount; i++)
                {
                    builder.Append(',');
                }
                builder.Append(']');
                if (type.GetElementType() is not { } elementType)
                {
                    break;
                }
                type = elementType;
            }
        }

        private static void ProcessGenericType(StyledStringBuilder builder, Type type, Type[] genericArguments, int length, DisplayNameOptions options, SyntaxHighlighter highlighter)
        {
            var offset = 0;
            if (type.IsNested && type.DeclaringType is not null)
            {
                offset = type.DeclaringType.GetGenericArguments().Length;
            }

            if (options.FullName)
            {
                if (type.IsNested && type.DeclaringType is not null)
                {
                    ProcessGenericType(builder, type.DeclaringType, genericArguments, offset, options, highlighter);
                    builder.Append('+');
                }
                else if (!string.IsNullOrEmpty(type.Namespace))
                {
                    builder.Append(type.Namespace);
                    builder.Append('.');
                }
            }

            var format = TypeNameFormatter.GetTypeStyle(type, highlighter);

            var genericPartIndex = type.Name.IndexOf('`');
            if (genericPartIndex <= 0)
            {
                builder.Append(type.Name, format);
                return;
            }

            if (type.Assembly.ManifestModule.Name == "FSharp.Core.dll" &&
                TypeNameHelper.FSharpTypeNames.TryGetValue(type.Name, out var builtInName))
            {
                builder.Append(builtInName, format);
            }
            else
            {
                builder.Append(type.Name.AsSpan(0, genericPartIndex).ToString(), format);
            }

            builder.Append('<');
            for (var i = offset; i < length; i++)
            {
                ProcessType(builder, genericArguments[i], options, tupleNames: null, highlighter);
                if (i + 1 == length)
                {
                    continue;
                }

                builder.Append(',');
                if (options.IncludeGenericParameterNames || !genericArguments[i + 1].IsGenericParameter)
                {
                    builder.Append(' ');
                }
            }
            builder.Append('>');
        }

        private readonly struct DisplayNameOptions
        {
            public readonly bool FullName;
            public readonly bool IncludeGenericParameterNames;

            public DisplayNameOptions(bool fullName, bool includeGenericParameterNames)
            {
                FullName = fullName;
                IncludeGenericParameterNames = includeGenericParameterNames;
            }
        }
    }
}