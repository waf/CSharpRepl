// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

#nullable disable

using System;
using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Text;
using Microsoft.CodeAnalysis.PooledObjects;

namespace Microsoft.CodeAnalysis.CSharp;

#pragma warning disable CA1200 // Avoid using cref tags with a prefix
/// <summary>
/// Displays a value in the C# style.
/// </summary>
/// <remarks>
/// Separate from <see cref="T:Microsoft.CodeAnalysis.CSharp.SymbolDisplay"/> because we want to link this functionality into
/// the Formatter project and we don't want it to be public there.
/// </remarks>
/// <seealso cref="T:Microsoft.CodeAnalysis.VisualBasic.ObjectDisplay.ObjectDisplay"/>
#pragma warning restore CA1200 // Avoid using cref tags with a prefix
internal static class ObjectDisplay
{
    internal static string FormatLiteral(bool value)
    {
        return value ? "true" : "false";
    }

    /// <summary>
    /// Returns true if the character should be replaced and sets
    /// <paramref name="replaceWith"/> to the replacement text.
    /// </summary>
    private static bool TryReplaceChar(char c, out string replaceWith)
    {
        replaceWith = null;
        switch (c)
        {
            case '\\':
                replaceWith = "\\\\";
                break;
            case '\0':
                replaceWith = "\\0";
                break;
            case '\a':
                replaceWith = "\\a";
                break;
            case '\b':
                replaceWith = "\\b";
                break;
            case '\f':
                replaceWith = "\\f";
                break;
            case '\n':
                replaceWith = "\\n";
                break;
            case '\r':
                replaceWith = "\\r";
                break;
            case '\t':
                replaceWith = "\\t";
                break;
            case '\v':
                replaceWith = "\\v";
                break;
        }

        if (replaceWith != null)
        {
            return true;
        }

        if (NeedsEscaping(CharUnicodeInfo.GetUnicodeCategory(c)))
        {
            replaceWith = "\\u" + ((int)c).ToString("x4");
            return true;
        }

        return false;
    }

    private static bool NeedsEscaping(UnicodeCategory category)
    {
        switch (category)
        {
            case UnicodeCategory.Control:
            case UnicodeCategory.OtherNotAssigned:
            case UnicodeCategory.ParagraphSeparator:
            case UnicodeCategory.LineSeparator:
            case UnicodeCategory.Surrogate:
                return true;
            default:
                return false;
        }
    }

    /// <summary>
    /// Returns a C# string literal with the given value.
    /// </summary>
    /// <param name="value">The value that the resulting string literal should have.</param>
    /// <param name="options">Options used to customize formatting of an object value.</param>
    /// <returns>A string literal with the given value.</returns>
    /// <remarks>
    /// Optionally escapes non-printable characters.
    /// </remarks>
    public static string FormatLiteral(string value, ObjectDisplayOptions options)
    {
        if (value == null)
        {
            throw new ArgumentNullException(nameof(value));
        }

        const char quote = '"';

        var pooledBuilder = PooledStringBuilder.GetInstance();
        var builder = pooledBuilder.Builder;

        var useQuotes = options.IncludesOption(ObjectDisplayOptions.UseQuotes);
        var escapeNonPrintable = options.IncludesOption(ObjectDisplayOptions.EscapeNonPrintableCharacters);

        var isVerbatim = useQuotes && !escapeNonPrintable && ContainsNewLine(value);

        if (useQuotes)
        {
            if (isVerbatim)
            {
                builder.Append('@');
            }
            builder.Append(quote);
        }

        for (int i = 0; i < value.Length; i++)
        {
            char c = value[i];
            if (escapeNonPrintable && CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.Surrogate)
            {
                var category = CharUnicodeInfo.GetUnicodeCategory(value, i);
                if (category == UnicodeCategory.Surrogate)
                {
                    // an unpaired surrogate
                    builder.Append("\\u" + ((int)c).ToString("x4"));
                }
                else if (NeedsEscaping(category))
                {
                    // a surrogate pair that needs to be escaped
                    var unicode = char.ConvertToUtf32(value, i);
                    builder.Append("\\U" + unicode.ToString("x8"));
                    i++; // skip the already-encoded second surrogate of the pair
                }
                else
                {
                    // copy a printable surrogate pair directly
                    builder.Append(c);
                    builder.Append(value[++i]);
                }
            }
            else if (escapeNonPrintable && TryReplaceChar(c, out var replaceWith))
            {
                builder.Append(replaceWith);
            }
            else if (useQuotes && c == quote)
            {
                if (isVerbatim)
                {
                    builder.Append(quote);
                    builder.Append(quote);
                }
                else
                {
                    builder.Append('\\');
                    builder.Append(quote);
                }
            }
            else
            {
                builder.Append(c);
            }
        }

        if (useQuotes)
        {
            builder.Append(quote);
        }

        return pooledBuilder.ToStringAndFree();
    }

    private static bool ContainsNewLine(string s)
    {
        foreach (char c in s)
        {
            if (SyntaxFacts.IsNewLine(c))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Returns a C# character literal with the given value.
    /// </summary>
    /// <param name="c">The value that the resulting character literal should have.</param>
    /// <param name="options">Options used to customize formatting of an object value.</param>
    /// <returns>A character literal with the given value.</returns>
    internal static string FormatLiteral(char c, ObjectDisplayOptions options)
    {
        const char quote = '\'';

        var pooledBuilder = PooledStringBuilder.GetInstance();
        var builder = pooledBuilder.Builder;

        if (options.IncludesOption(ObjectDisplayOptions.IncludeCodePoints))
        {
            builder.Append(options.IncludesOption(ObjectDisplayOptions.UseHexadecimalNumbers) ? "0x" + ((int)c).ToString("x4") : ((int)c).ToString());
            builder.Append(" ");
        }

        var useQuotes = options.IncludesOption(ObjectDisplayOptions.UseQuotes);
        var escapeNonPrintable = options.IncludesOption(ObjectDisplayOptions.EscapeNonPrintableCharacters);

        if (useQuotes)
        {
            builder.Append(quote);
        }

        string replaceWith;
        if (escapeNonPrintable && TryReplaceChar(c, out replaceWith))
        {
            builder.Append(replaceWith);
        }
        else if (useQuotes && c == quote)
        {
            builder.Append('\\');
            builder.Append(quote);
        }
        else
        {
            builder.Append(c);
        }

        if (useQuotes)
        {
            builder.Append(quote);
        }

        return pooledBuilder.ToStringAndFree();
    }

    internal static string FormatLiteral(sbyte value, ObjectDisplayOptions options, CultureInfo cultureInfo = null)
    {
        if (options.IncludesOption(ObjectDisplayOptions.UseHexadecimalNumbers))
        {
            // Special Case: for sbyte and short, specifically, negatives are shown
            // with extra precision.
            return "0x" + (value >= 0 ? value.ToString("x2") : ((int)value).ToString("x8"));
        }
        else
        {
            return value.ToString(GetFormatCulture(cultureInfo));
        }
    }

    internal static string FormatLiteral(byte value, ObjectDisplayOptions options, CultureInfo cultureInfo = null)
    {
        if (options.IncludesOption(ObjectDisplayOptions.UseHexadecimalNumbers))
        {
            return "0x" + value.ToString("x2");
        }
        else
        {
            return value.ToString(GetFormatCulture(cultureInfo));
        }
    }

    internal static string FormatLiteral(short value, ObjectDisplayOptions options, CultureInfo cultureInfo = null)
    {
        if (options.IncludesOption(ObjectDisplayOptions.UseHexadecimalNumbers))
        {
            // Special Case: for sbyte and short, specifically, negatives are shown
            // with extra precision.
            return "0x" + (value >= 0 ? value.ToString("x4") : ((int)value).ToString("x8"));
        }
        else
        {
            return value.ToString(GetFormatCulture(cultureInfo));
        }
    }

    internal static string FormatLiteral(ushort value, ObjectDisplayOptions options, CultureInfo cultureInfo = null)
    {
        if (options.IncludesOption(ObjectDisplayOptions.UseHexadecimalNumbers))
        {
            return "0x" + value.ToString("x4");
        }
        else
        {
            return value.ToString(GetFormatCulture(cultureInfo));
        }
    }

    internal static string FormatLiteral(int value, ObjectDisplayOptions options, CultureInfo cultureInfo = null)
    {
        if (options.IncludesOption(ObjectDisplayOptions.UseHexadecimalNumbers))
        {
            return "0x" + value.ToString("x8");
        }
        else
        {
            return value.ToString(GetFormatCulture(cultureInfo));
        }
    }

    internal static string FormatLiteral(uint value, ObjectDisplayOptions options, CultureInfo cultureInfo = null)
    {
        var pooledBuilder = PooledStringBuilder.GetInstance();
        var sb = pooledBuilder.Builder;

        if (options.IncludesOption(ObjectDisplayOptions.UseHexadecimalNumbers))
        {
            sb.Append("0x");
            sb.Append(value.ToString("x8"));
        }
        else
        {
            sb.Append(value.ToString(GetFormatCulture(cultureInfo)));
        }

        if (options.IncludesOption(ObjectDisplayOptions.IncludeTypeSuffix))
        {
            sb.Append('U');
        }

        return pooledBuilder.ToStringAndFree();
    }

    internal static string FormatLiteral(long value, ObjectDisplayOptions options, CultureInfo cultureInfo = null)
    {
        var pooledBuilder = PooledStringBuilder.GetInstance();
        var sb = pooledBuilder.Builder;

        if (options.IncludesOption(ObjectDisplayOptions.UseHexadecimalNumbers))
        {
            sb.Append("0x");
            sb.Append(value.ToString("x16"));
        }
        else
        {
            sb.Append(value.ToString(GetFormatCulture(cultureInfo)));
        }

        if (options.IncludesOption(ObjectDisplayOptions.IncludeTypeSuffix))
        {
            sb.Append('L');
        }

        return pooledBuilder.ToStringAndFree();
    }

    internal static string FormatLiteral(ulong value, ObjectDisplayOptions options, CultureInfo cultureInfo = null)
    {
        var pooledBuilder = PooledStringBuilder.GetInstance();
        var sb = pooledBuilder.Builder;

        if (options.IncludesOption(ObjectDisplayOptions.UseHexadecimalNumbers))
        {
            sb.Append("0x");
            sb.Append(value.ToString("x16"));
        }
        else
        {
            sb.Append(value.ToString(GetFormatCulture(cultureInfo)));
        }

        if (options.IncludesOption(ObjectDisplayOptions.IncludeTypeSuffix))
        {
            sb.Append("UL");
        }

        return pooledBuilder.ToStringAndFree();
    }

    internal static string FormatLiteral(double value, ObjectDisplayOptions options, CultureInfo cultureInfo = null)
    {
        var result = value.ToString("R", GetFormatCulture(cultureInfo));

        return options.IncludesOption(ObjectDisplayOptions.IncludeTypeSuffix) ? result + "D" : result;
    }

    internal static string FormatLiteral(float value, ObjectDisplayOptions options, CultureInfo cultureInfo = null)
    {
        var result = value.ToString("R", GetFormatCulture(cultureInfo));

        return options.IncludesOption(ObjectDisplayOptions.IncludeTypeSuffix) ? result + "F" : result;
    }

    internal static string FormatLiteral(decimal value, ObjectDisplayOptions options, CultureInfo cultureInfo = null)
    {
        var result = value.ToString(GetFormatCulture(cultureInfo));

        return options.IncludesOption(ObjectDisplayOptions.IncludeTypeSuffix) ? result + "M" : result;
    }

    private static CultureInfo GetFormatCulture(CultureInfo cultureInfo)
    {
        return cultureInfo ?? CultureInfo.InvariantCulture;
    }
}