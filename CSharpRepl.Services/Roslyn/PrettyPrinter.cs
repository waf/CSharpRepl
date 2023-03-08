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
using static Microsoft.CodeAnalysis.Scripting.Hosting.CommonObjectFormatter;

namespace CSharpRepl.Services.Roslyn;

internal sealed partial class PrettyPrinter
{
    private static readonly ICustomObjectFormatter[] customObjectFormatters = new ICustomObjectFormatter[]
    {
        TypeFormatter.Instance,
        MethodInfoFormatter.Instance,
        TupleFormatter.Instance
    };

    private readonly CSharpObjectFormatterImpl formatter;
    private readonly PrintOptions singleLineOptions;
    private readonly PrintOptions multiLineOptions;
    private readonly SyntaxHighlighter syntaxHighlighter;
    private readonly Configuration config;

    public StyledStringSegment NullLiteral => formatter.NullLiteral;

    public PrettyPrinter(SyntaxHighlighter syntaxHighlighter, Configuration config)
    {
        formatter = new CSharpObjectFormatterImpl(syntaxHighlighter, config);
        singleLineOptions = new PrintOptions
        {
            MemberDisplayFormat = MemberDisplayFormat.SingleLine,
            MaximumOutputLength = 20_000,
        };
        multiLineOptions = new PrintOptions
        {
            MemberDisplayFormat = MemberDisplayFormat.SeparateLines,
            MaximumOutputLength = 20_000,
        };
        this.syntaxHighlighter = syntaxHighlighter;
        this.config = config;
    }

    public FormattedObject FormatObject(object? obj, Level level)
    {
        return obj switch
        {
            null => new FormattedObject(formatter.NullLiteral.ToParagraph(), value: null),

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
                new CommonTypeNameFormatterOptions(
                    arrayBoundRadix: singleLineOptions.NumberRadix,
                    showNamespaces,
                    useLanguageKeywords));

    public StyledString FormatObjectSafeToStyledString(object? obj, Level level, bool? quoteStringsAndCharacters)
        => FormatObjectSafe<StyledString>(
            obj,
            level == Level.FirstDetailed ? multiLineOptions : singleLineOptions,
            level,
            quoteStringsAndCharacters,
            customObjectFormat: (customFormatter, obj, level, formatter) => customFormatter.FormatToText(obj, level, formatter),
            styledStringToResult: styledString => styledString,
            styledStringSegmentToResult: styledStringSegment => styledStringSegment);

    private IRenderable FormatObjectSafeToRenderable(object? obj, Level level)
        => FormatObjectSafe<IRenderable>(
            obj,
            level == Level.FirstDetailed ? multiLineOptions : singleLineOptions,
            level,
            quoteStringsAndCharacters: null,
            customObjectFormat: (customFormatter, obj, level, formatter) => customFormatter.FormatToText(obj, level, formatter).ToParagraph(), //TODO - Hubert ICustomObjectFormatter.FormatToRenderable
            styledStringToResult: styledString => styledString.ToParagraph(),
            styledStringSegmentToResult: styledStringSegment => styledStringSegment.ToParagraph());

    private TResult FormatObjectSafe<TResult>(
        object? obj,
        PrintOptions options,
        Level level,
        bool? quoteStringsAndCharacters,
        Func<ICustomObjectFormatter, object, Level, Formatter, TResult> customObjectFormat,
        Func<StyledString, TResult> styledStringToResult,
        Func<StyledStringSegment, TResult> styledStringSegmentToResult)
    {
        if (obj is null)
        {
            return styledStringSegmentToResult(formatter.PrimitiveFormatter.NullLiteral);
        }

        try
        {
            var BuilderOptions = GetInternalBuilderOptions(options);
            var PrimitiveOptions = GetPrimitiveOptions(options);
            var TypeNameOptions = GetTypeNameOptions(options, level);

            var oldPrimitiveOptions = PrimitiveOptions;
            if (quoteStringsAndCharacters.HasValue)
            {
                PrimitiveOptions = new CommonPrimitiveFormatterOptions(
                        PrimitiveOptions.NumberRadix,
                        PrimitiveOptions.IncludeCharacterCodePoints,
                        quoteStringsAndCharacters: quoteStringsAndCharacters.Value,
                        escapeNonPrintableCharacters: PrimitiveOptions.EscapeNonPrintableCharacters,
                        cultureInfo: PrimitiveOptions.CultureInfo);
            }

            var primitive = formatter.PrimitiveFormatter.FormatPrimitive(obj, PrimitiveOptions);
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
                    return styledStringToResult(GetValueRetrievalExceptionText(ex));
                }
            }
            else
            {
                return styledStringToResult(formatter.TypeNameFormatter.FormatTypeName(type, TypeNameOptions));
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
                return styledStringToResult(GetValueRetrievalExceptionText(ex));
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
                            sb.Append(GetValueRetrievalExceptionText(exception));
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

    private BuilderOptions GetInternalBuilderOptions(PrintOptions printOptions) =>
        new(
            indentation: "  ",
            newLine: Environment.NewLine,
            ellipsis: printOptions.Ellipsis,
            maximumLineLength: int.MaxValue,
            maximumOutputLength: printOptions.MaximumOutputLength);

    private CommonPrimitiveFormatterOptions GetPrimitiveOptions(PrintOptions printOptions) =>
        new(
            numberRadix: printOptions.NumberRadix,
            includeCodePoints: false,
            quoteStringsAndCharacters: true,
            escapeNonPrintableCharacters: printOptions.EscapeNonPrintableCharacters,
            cultureInfo: CultureInfo.CurrentUICulture);

    private CommonTypeNameFormatterOptions GetTypeNameOptions(PrintOptions printOptions, Level level) =>
        new(
            arrayBoundRadix: printOptions.NumberRadix,
            showNamespaces: level == Level.FirstDetailed);
}