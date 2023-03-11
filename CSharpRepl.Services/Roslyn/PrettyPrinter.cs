// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Globalization;
using System.Linq;
using CSharpRepl.Services.Extensions;
using CSharpRepl.Services.Roslyn.CustomObjectFormatters;
using CSharpRepl.Services.SyntaxHighlighting;
using CSharpRepl.Services.Theming;
using Microsoft.CodeAnalysis.CSharp.Scripting.Hosting;
using Microsoft.CodeAnalysis.Scripting;
using Microsoft.CodeAnalysis.Scripting.Hosting;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace CSharpRepl.Services.Roslyn;

internal sealed partial class PrettyPrinter
{
    private const int NumberRadix = 10;

    private static readonly ICustomObjectFormatter[] customObjectFormatters = new ICustomObjectFormatter[]
    {
        TypeFormatter.Instance,
        MethodInfoFormatter.Instance,
        TupleFormatter.Instance
    };

    private readonly CSharpObjectFormatterImpl formatter;
    private readonly SyntaxHighlighter syntaxHighlighter;
    private readonly Configuration config;

    public StyledStringSegment NullLiteral => formatter.NullLiteral;

    public PrettyPrinter(SyntaxHighlighter syntaxHighlighter, Configuration config)
    {
        formatter = new CSharpObjectFormatterImpl(syntaxHighlighter, config);
        this.syntaxHighlighter = syntaxHighlighter;
        this.config = config;
    }

    public FormattedObject FormatObject(object? obj, Level level)
    {
        return obj switch
        {
            null => new FormattedObject(NullLiteral.ToParagraph(), value: null),

            // when detailed is true, don't show the escaped string (i.e. interpret the escape characters, via displaying to console)
            string str when level == 0 => new FormattedObject(
                new Paragraph(str),
                value: str),

            //call stack for compilation error exception is useless
            CompilationErrorException compilationErrorException => new FormattedObject(new Paragraph(compilationErrorException.Message), value: null),

            Exception exception => new FormattedObject(FormatException(exception, level).ToParagraph(), value: exception),

            _ => new FormattedObject(FormatObjectSafeToRenderable(obj, level), obj)
        };
    }

    public StyledString FormatTypeName(Type type, bool showNamespaces, bool useLanguageKeywords)
        => formatter.TypeNameFormatter.FormatTypeName(
                type,
                new TypeNameFormatterOptions(
                    arrayBoundRadix: NumberRadix,
                    showNamespaces,
                    useLanguageKeywords));

    public StyledString FormatObjectSafeToStyledString(object? obj, Level level, bool? quoteStringsAndCharacters)
        => FormatObjectSafe<StyledString>(
            obj,
            level,
            quoteStringsAndCharacters,
            customObjectFormat: (customFormatter, obj, level, formatter) => customFormatter.FormatToText(obj, level, formatter),
            styledStringToResult: styledString => styledString,
            styledStringSegmentToResult: styledStringSegment => styledStringSegment);

    private IRenderable FormatObjectSafeToRenderable(object? obj, Level level)
        => FormatObjectSafe<IRenderable>(
            obj,
            level,
            quoteStringsAndCharacters: null,
            customObjectFormat: (customFormatter, obj, level, formatter) => customFormatter.FormatToText(obj, level, formatter).ToParagraph(),
            styledStringToResult: styledString => styledString.ToParagraph(), //TODO - Hubert ICustomObjectFormatter.FormatToRenderable
            styledStringSegmentToResult: styledStringSegment => styledStringSegment.ToParagraph());

    private TResult FormatObjectSafe<TResult>(
        object? obj,
        Level level,
        bool? quoteStringsAndCharacters,
        Func<ICustomObjectFormatter, object, Level, Formatter, TResult> customObjectFormat,
        Func<StyledString, TResult> styledStringToResult,
        Func<StyledStringSegment, TResult> styledStringSegmentToResult)
    {
        if (obj is null)
        {
            return styledStringSegmentToResult(NullLiteral);
        }

        try
        {
            var primitiveOptions = GetPrimitiveOptions(quoteStringsAndCharacters ?? true);
            var primitive = formatter.PrimitiveFormatter.FormatPrimitive(obj, primitiveOptions);
            if (primitive.TryGet(out var primitiveValue))
            {
                return styledStringSegmentToResult(primitiveValue);
            }

            var type = obj.GetType();
            if (customObjectFormatters.FirstOrDefault(f => f.IsApplicable(obj)).TryGet(out var customFormatter))
            {
                return customObjectFormat(customFormatter, obj, level, new Formatter(this, syntaxHighlighter));
            }

            if (ObjectFormatterHelpers.GetApplicableDebuggerDisplayAttribute(type)?.Value is { } debuggerDisplayFormat)
            {
                var formattedValue = FormatWithEmbeddedExpressions(debuggerDisplayFormat, obj, level);
                return
                    level is Level.FirstDetailed or Level.FirstSimple ?
                    styledStringToResult(("(" + formattedValue + ")")) :
                    styledStringToResult(formattedValue);
            }
            else if (ObjectFormatterHelpers.HasOverriddenToString(type))
            {
                try
                {
                    return styledStringSegmentToResult($"[{obj}]");
                }
                catch (Exception ex)
                {
                    return styledStringToResult(GetValueRetrievalExceptionText(ex, level));
                }
            }
            else
            {
                var typeNameOptions = GetTypeNameOptions(level);
                return styledStringToResult(formatter.TypeNameFormatter.FormatTypeName(type, typeNameOptions));
            }
        }
        catch
        {
            try
            {
                return styledStringSegmentToResult(obj.ToString() ?? "");
            }
            catch (Exception ex)
            {
                return styledStringToResult(GetValueRetrievalExceptionText(ex, level));
            }
        }
    }

    private StyledString FormatWithEmbeddedExpressions(string format, object obj, Level level)
    {
        var sb = new StyledStringBuilder();
        int i = 0;
        while (i < format.Length)
        {
            char c = format[i++];
            if (c == '{')
            {
                if (i >= 2 && format[i - 2] == '\\')
                {
                    sb.Append('{');
                }
                else
                {
                    int expressionEnd = format.IndexOf('}', i);

                    string memberName;
                    if (expressionEnd == -1 || (memberName = ObjectFormatterHelpers.ParseSimpleMemberName(format, i, expressionEnd, out bool noQuotes, out bool callableOnly)) == null)
                    {
                        // the expression isn't properly formatted
                        sb.Append(format.AsSpan(i - 1, format.Length - i + 1).ToString());
                        break;
                    }

                    var member = ObjectFormatterHelpers.ResolveMember(obj, memberName, callableOnly);
                    if (member == null)
                    {
                        sb.Append(GetErrorText($"{(callableOnly ? "Method" : "Member")} '{memberName}' not found"));
                    }
                    else
                    {
                        var value = ObjectFormatterHelpers.GetMemberValue(obj, member, out var exception);
                        if (exception != null)
                        {
                            sb.Append(GetValueRetrievalExceptionText(exception, level));
                        }
                        else
                        {
                            sb.Append(FormatObjectSafeToStyledString(value, level.Increment(), quoteStringsAndCharacters: !noQuotes));
                        }
                    }
                    i = expressionEnd + 1;
                }
            }
            else
            {
                sb.Append(c);
            }
        }

        return sb.ToStyledString();
    }

    private PrimitiveFormatterOptions GetPrimitiveOptions(bool quoteStringsAndCharacters) => new(
        numberRadix: NumberRadix,
        includeCodePoints: false,
        quoteStringsAndCharacters: quoteStringsAndCharacters,
        escapeNonPrintableCharacters: true,
        cultureInfo: CultureInfo.CurrentUICulture);

    private TypeNameFormatterOptions GetTypeNameOptions(Level level) => new(
        arrayBoundRadix: NumberRadix,
        showNamespaces: level == Level.FirstDetailed);
}