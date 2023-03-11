// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections;
using System.Reflection;
using CSharpRepl.Services.Theming;

namespace CSharpRepl.Services.Roslyn.Formatting.CustomObjectFormatters;

internal class IEnumerableFormatter : CustomObjectFormatter<IEnumerable>
{
    public static readonly IEnumerableFormatter Instance = new();

    private IEnumerableFormatter() { }

    public override StyledString Format(IEnumerable value, Level level, Formatter formatter)
    {
        var sb = new StyledStringBuilder();

        //header
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