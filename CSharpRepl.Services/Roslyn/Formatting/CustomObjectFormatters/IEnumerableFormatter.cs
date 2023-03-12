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

internal class IEnumerableFormatter : CustomObjectFormatter<IEnumerable>
{
    public static readonly IEnumerableFormatter Instance = new();

    private IEnumerableFormatter() { }

    public override StyledString FormatToText(IEnumerable value, Level level, Formatter formatter)
    {
        var sb = new StyledStringBuilder();

        //header
        AppendHeader(sb, value, formatter);

        //items
        sb.Append(" { ");
        var enumerator = value.GetEnumerator();
        try
        {
            bool any = false;
            while (enumerator.MoveNext())
            {
                if (any) sb.Append(", ");
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

        return sb.ToStyledString();
    }

    public override FormattedObjectRenderable FormatToRenderable(IEnumerable value, Level level, Formatter formatter)
    {
        if (level >= Level.Second)
        {
            return new FormattedObjectRenderable(FormatToText(value, level, formatter).ToParagraph(), renderOnNewLine: false);
        }

        var sb = new StyledStringBuilder();

        AppendHeader(sb, value, formatter);
        var header = sb.ToStyledString().ToParagraph();

        var table = new Table().AddColumns("Name", "Value", "Type");

        var enumerator = value.GetEnumerator();
        try
        {
            int counter = 0;
            while (enumerator.MoveNext())
            {
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

    private static void AppendHeader(StyledStringBuilder sb, IEnumerable value, Formatter formatter)
    {
        var isArray = value is Array;
        sb.Append(
            formatter.FormatTypeName(
                isArray ? (value.GetType().GetElementType() ?? value.GetType()) : value.GetType(),
                showNamespaces: false,
                useLanguageKeywords: true));

        if (TryGetCount(value, formatter, out var count))
        {
            sb.Append(isArray ? '[' : '(');
            sb.Append(count);
            sb.Append(isArray ? ']' : ')');
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