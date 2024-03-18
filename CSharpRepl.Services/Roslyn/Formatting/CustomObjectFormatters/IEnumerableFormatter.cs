// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections;
using System.Reflection;
using CSharpRepl.Services.Roslyn.Formatting.Rendering;
using CSharpRepl.Services.Theming;
using Spectre.Console;

namespace CSharpRepl.Services.Roslyn.Formatting.CustomObjectFormatters;

internal sealed class IEnumerableFormatter : CustomObjectFormatter<IEnumerable>
{
    public static readonly IEnumerableFormatter Instance = new();

    private IEnumerableFormatter() { }

    public override StyledString FormatToText(IEnumerable value, Level level, Formatter formatter)
    {
        var sb = new StyledStringBuilder();

        //header
        if (AppendHeader(sb, value, formatter))
        {
            //items
            sb.Append(" { ");
            var enumerator = value.GetEnumerator();
            try
            {
                var maxParagraphLength = LengthLimiting.GetMaxParagraphLength(level, formatter.ConsoleProfile);
                bool any = false;
                while (enumerator.MoveNext())
                {
                    if (any)
                    {
                        sb.Append(", ");

                        if (maxParagraphLength > sb.Length &&
                            sb.Length > formatter.ConsoleProfile.Width / 2) //just heuristic
                        {
                            sb.Append(", ...");
                            break;
                        }
                    }
                    var formattedItem = formatter.FormatObjectToText(enumerator.Current, level.Increment());
                    sb.Append(formattedItem);
                    any = true;
                }
            }
            catch (Exception ex)
            {
                sb.Append(formatter.GetValueRetrievalExceptionText(ex, level.Increment()));
            }
            finally
            {
                if (enumerator is IDisposable disposable)
                {
                    disposable.Dispose();
                }
            }
            sb.Append(" }");
        }

        return sb.ToStyledString();
    }

    public override FormattedObjectRenderable FormatToRenderable(IEnumerable value, Level level, Formatter formatter)
    {
        if (level >= Level.Second)
        {
            return new FormattedObjectRenderable(FormatToText(value, level, formatter).ToParagraph(), renderOnNewLine: false);
        }

        var sb = new StyledStringBuilder();
        if (AppendHeader(sb, value, formatter))
        {
            var header = sb.ToStyledString().ToParagraph();
            var table = new Table().AddColumns("Name", "Value", "Type");

            var enumerator = value.GetEnumerator();
            try
            {
                var maxItems = LengthLimiting.GetTableMaxItems(level, formatter.ConsoleProfile);
                int counter = 0;
                while (enumerator.MoveNext())
                {
                    if (counter > maxItems)
                    {
                        table.AddRow("...", "...", "...");
                        break;
                    }

                    sb.Clear();
                    sb.Append('[').Append(formatter.FormatObjectToText(counter, Level.FirstSimple)).Append(']');

                    var name = sb.ToStyledString();

                    var itemValue = formatter.FormatObjectToRenderable(enumerator.Current, level.Increment());

                    var itemType =
                        enumerator.Current is null ?
                        new Paragraph("") :
                        formatter.FormatObjectToText(enumerator.Current.GetType(), level.Increment()).ToParagraph();

                    table.AddRow(name.ToParagraph(), itemValue, itemType);

                    counter++;
                }

                if (counter == 0)
                {
                    return new FormattedObjectRenderable(header, renderOnNewLine: false);
                }
            }
            catch (Exception ex)
            {
                table.AddRow(new Paragraph(""), formatter.GetValueRetrievalExceptionText(ex, level.Increment()).ToParagraph(), new Paragraph(""));
            }
            finally
            {
                if (enumerator is IDisposable disposable)
                {
                    disposable.Dispose();
                }
            }

            return new FormattedObjectRenderable(
                new RenderableSequence(header, table, separateByLineBreak: true),
                renderOnNewLine: false);
        }
        else
        {
            return new FormattedObjectRenderable(sb.ToStyledString().ToParagraph(), renderOnNewLine: false);
        }
    }

    private static bool AppendHeader(StyledStringBuilder sb, IEnumerable value, Formatter formatter)
    {
        var type = value.GetType();

        if (type.FullName?.EndsWith(typeof(__CSharpRepl_RuntimeHelper.CharSpanOutput).FullName!) == true)
        {
            if (type.GetField(nameof(__CSharpRepl_RuntimeHelper.CharSpanOutput.Text))?.GetValue(value) is string text &&
                type.GetField(nameof(__CSharpRepl_RuntimeHelper.CharSpanOutput.SpanWasReadOnly))?.GetValue(value) is bool readOnly)
            {
                type = readOnly ? typeof(ReadOnlySpan<char>) : typeof(Span<char>);

                AppendTypeName(isArray: false);
                sb.Append(Environment.NewLine);
                sb.Append(formatter.FormatObjectToText(text, Level.FirstDetailed));
                return false; 
            }
        }
        else if (type.FullName?.EndsWith(typeof(__CSharpRepl_RuntimeHelper.SpanOutput).FullName!) == true)
        {
            if (type.GetField(nameof(__CSharpRepl_RuntimeHelper.SpanOutput.Array))?.GetValue(value)  is Array array &&
                type.GetField(nameof(__CSharpRepl_RuntimeHelper.SpanOutput.SpanWasReadOnly))?.GetValue(value) is bool readOnly)
            {
                type = (readOnly ? typeof(ReadOnlySpan<>) : typeof(Span<>)).MakeGenericType(array.GetType().GetElementType() ?? array.GetType());
            }
        }

        var isArray = value is Array;
        AppendTypeName(isArray);
        return true;

        void AppendTypeName(bool isArray)
        {
            sb.Append(
                formatter.FormatTypeName(
                    isArray ? (type.GetElementType() ?? type) : type,
                    showNamespaces: false,
                    useLanguageKeywords: true,
                    hideSystemNamespace: true));

            if (TryGetCount(value, formatter, out var count))
            {
                sb.Append(isArray ? '[' : '(');
                sb.Append(count);
                sb.Append(isArray ? ']' : ')');
            }
        }
    }

    private static bool TryGetCount(IEnumerable value, Formatter formatter, out StyledString count)
    {
        if (value.GetType().GetMember("Count", BindingFlags.Instance | BindingFlags.Public) is [PropertyInfo countProperty])
        {
            var result = FormatNumericValue(countProperty.GetValue(value), formatter);
            if (result.Length > 0)
            {
                count = result;
                return true;
            }
        }
        if (value.GetType().GetMember("Length", BindingFlags.Instance | BindingFlags.Public) is [PropertyInfo lengthProperty])
        {
            var result = FormatNumericValue(lengthProperty.GetValue(value), formatter);
            if (result.Length > 0)
            {
                count = result;
                return true;
            }
        }
        count = StyledString.Empty;
        return false;
    }

    private static StyledString FormatNumericValue(object? numericValue, Formatter formatter)
    {
        if (numericValue is int) return formatter.FormatObjectToText((int)numericValue, Level.FirstSimple);
        if (numericValue is long) return formatter.FormatObjectToText((long)numericValue, Level.FirstSimple);
        if (numericValue is byte) return formatter.FormatObjectToText((byte)numericValue, Level.FirstSimple);
        if (numericValue is decimal) return formatter.FormatObjectToText((decimal)numericValue, Level.FirstSimple);
        if (numericValue is uint) return formatter.FormatObjectToText((uint)numericValue, Level.FirstSimple);
        if (numericValue is ulong) return formatter.FormatObjectToText((ulong)numericValue, Level.FirstSimple);
        if (numericValue is short) return formatter.FormatObjectToText((short)numericValue, Level.FirstSimple);
        if (numericValue is ushort) return formatter.FormatObjectToText((ushort)numericValue, Level.FirstSimple);
        if (numericValue is sbyte) return formatter.FormatObjectToText((sbyte)numericValue, Level.FirstSimple);

        return StyledString.Empty;
    }
}