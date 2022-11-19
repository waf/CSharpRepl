// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Text.RegularExpressions;
using CSharpRepl.Services;
using CSharpRepl.Services.SyntaxHighlighting;
using Microsoft.CodeAnalysis.PooledObjects;
using PrettyPrompt.Highlighting;
using static Microsoft.CodeAnalysis.Scripting.Hosting.ObjectFormatterHelpers;

namespace Microsoft.CodeAnalysis.Scripting.Hosting;

/// <summary>
/// Object pretty printer.
/// </summary>
internal abstract partial class CommonObjectFormatter
{
    private readonly SyntaxHighlighter syntaxHighlighter;
    private readonly Configuration config;

    protected CommonObjectFormatter(SyntaxHighlighter syntaxHighlighter, Configuration config)
    {
        this.syntaxHighlighter = syntaxHighlighter;
        this.config = config;
    }

    public FormattedString FormatObject(object obj, PrintOptions options)
    {
        if (options == null)
        {
            // We could easily recover by using default options, but it makes
            // more sense for the host to choose the defaults so we'll require
            // that options be passed.
            throw new ArgumentNullException(nameof(options));
        }

        var visitor = new Visitor(this, GetInternalBuilderOptions(options), GetPrimitiveOptions(options), GetTypeNameOptions(options), options.MemberDisplayFormat, syntaxHighlighter, config);
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

    public FormattedString FormatException(Exception e)
    {
        if (e == null)
        {
            throw new ArgumentNullException(nameof(e));
        }

        var pooled = PooledStringBuilder.GetInstance();
        var builder = pooled.Builder;

        builder.Append(e.GetType());
        builder.Append(": ");
        builder.Append(e.Message);

        var frames = (EnhancedStackFrame[])new EnhancedStackTrace(e).GetFrames();
        var count = frames.Length;
        const int ScriptRunnerMethods = 5;
        for (var i = 0; i < count - ScriptRunnerMethods; i++)
        {
            builder.Append(Environment.NewLine);

            var frame = frames[i];
            builder.Append("   at ");

            var methodSignature = frame.MethodInfo.ToString();
            methodSignature = Regex.Replace(methodSignature, @"Submission#[0-9]+\.", ""); //https://github.com/waf/CSharpRepl/issues/194
            builder.Append(methodSignature);

            if (frame.GetFileName() is { Length: > 0 } fileName)
            {
                builder.Append(" in ");
                builder.Append(EnhancedStackTrace.TryGetFullPath(fileName));
            }

            var lineNo = frame.GetFileLineNumber();
            if (lineNo != 0)
            {
                builder.Append(":line ");
                builder.Append(lineNo);
            }
        }

        return new FormattedString(pooled.ToStringAndFree(), new ConsoleFormat(AnsiColor.Red));
    }

    /// <summary>
    /// Returns a method signature display string. Used to display stack frames.
    /// </summary>
    /// <returns>Null if the method is a compiler generated method that shouldn't be displayed to the user.</returns>
    protected internal virtual string FormatMethodSignature(MethodBase method)
    {
        var pooled = PooledStringBuilder.GetInstance();
        var builder = pooled.Builder;

        var declaringType = method.DeclaringType;
        var options = new CommonTypeNameFormatterOptions(arrayBoundRadix: NumberRadixDecimal, showNamespaces: true);

        builder.Append(TypeNameFormatter.FormatTypeName(declaringType, options));
        builder.Append('.');
        builder.Append(method.Name);
        if (method.IsGenericMethod)
        {
            builder.Append(TypeNameFormatter.FormatTypeArguments(method.GetGenericArguments(), options));
        }

        builder.Append('(');

        bool first = true;
        foreach (var parameter in method.GetParameters())
        {
            if (first)
            {
                first = false;
            }
            else
            {
                builder.Append(", ");
            }

            if (parameter.ParameterType.IsByRef)
            {
                builder.Append(FormatRefKind(parameter));
                builder.Append(' ');
                builder.Append(TypeNameFormatter.FormatTypeName(parameter.ParameterType.GetElementType(), options));
            }
            else
            {
                builder.Append(TypeNameFormatter.FormatTypeName(parameter.ParameterType, options));
            }
        }

        builder.Append(')');

        return pooled.ToStringAndFree();
    }

    protected abstract string FormatRefKind(ParameterInfo parameter);
}