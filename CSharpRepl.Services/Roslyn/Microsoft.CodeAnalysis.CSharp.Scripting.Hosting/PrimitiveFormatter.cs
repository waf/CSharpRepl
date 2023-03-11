// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

//Modified version of
//  Microsoft.CodeAnalysis.CSharp.Scripting.Hosting.CSharpPrimitiveFormatter and
//  Microsoft.CodeAnalysis.Scripting.Hosting.CommonPrimitiveFormatter

using System;
using System.Globalization;
using System.Reflection;
using CSharpRepl.Services.SyntaxHighlighting;
using CSharpRepl.Services.Theming;
using Microsoft.CodeAnalysis.Classification;
using Microsoft.CodeAnalysis.Scripting.Hosting;
using Spectre.Console;

namespace Microsoft.CodeAnalysis.CSharp.Scripting.Hosting;

using static ObjectFormatterHelpers;

internal sealed class PrimitiveFormatter
{
    private readonly Style numericLiteralFormat;
    private readonly Style stringLiteralFormat;
    private readonly SyntaxHighlighter highlighter;

    public PrimitiveFormatter(SyntaxHighlighter highlighter)
    {
        numericLiteralFormat = highlighter.GetStyle(ClassificationTypeNames.NumericLiteral);
        stringLiteralFormat = highlighter.GetStyle(ClassificationTypeNames.StringLiteral);
        NullLiteral = new StyledStringSegment("null", highlighter.KeywordStyle);
        this.highlighter = highlighter;
    }

    public StyledStringSegment NullLiteral { get; }

    /// <summary>
    /// Returns null if the type is not considered primitive in the target language.
    /// </summary>
    public StyledStringSegment? FormatPrimitive(object? obj, PrimitiveFormatterOptions options)
    {
        if (ReferenceEquals(obj, VoidValue))
        {
            return string.Empty;
        }

        if (obj == null)
        {
            return NullLiteral;
        }

        var type = obj.GetType();

        if (type.GetTypeInfo().IsEnum)
        {
            return obj.ToString() ?? "";
        }

        switch (GetPrimitiveSpecialType(type))
        {
            case SpecialType.System_Int32:
                return FormatLiteral((int)obj, options.NumberRadix, options.CultureInfo);

            case SpecialType.System_String:
                return FormatLiteral((string)obj, options.QuoteStringsAndCharacters, options.EscapeNonPrintableCharacters, options.NumberRadix);

            case SpecialType.System_Boolean:
                return FormatLiteral((bool)obj);

            case SpecialType.System_Char:
                return FormatLiteral((char)obj, options.QuoteStringsAndCharacters, options.EscapeNonPrintableCharacters, options.IncludeCharacterCodePoints, options.NumberRadix);

            case SpecialType.System_Int64:
                return FormatLiteral((long)obj, options.NumberRadix, options.CultureInfo);

            case SpecialType.System_Double:
                return FormatLiteral((double)obj, options.CultureInfo);

            case SpecialType.System_Byte:
                return FormatLiteral((byte)obj, options.NumberRadix, options.CultureInfo);

            case SpecialType.System_Decimal:
                return FormatLiteral((decimal)obj, options.CultureInfo);

            case SpecialType.System_UInt32:
                return FormatLiteral((uint)obj, options.NumberRadix, options.CultureInfo);

            case SpecialType.System_UInt64:
                return FormatLiteral((ulong)obj, options.NumberRadix, options.CultureInfo);

            case SpecialType.System_Single:
                return FormatLiteral((float)obj, options.CultureInfo);

            case SpecialType.System_Int16:
                return FormatLiteral((short)obj, options.NumberRadix, options.CultureInfo);

            case SpecialType.System_UInt16:
                return FormatLiteral((ushort)obj, options.NumberRadix, options.CultureInfo);

            case SpecialType.System_DateTime:
                return null; // DateTime is not primitive in C#

            case SpecialType.System_SByte:
                return FormatLiteral((sbyte)obj, options.NumberRadix, options.CultureInfo);

            case SpecialType.System_Object:
            case SpecialType.System_Void:
            case SpecialType.None:
                return null;

            default:
                throw new InvalidOperationException($"Unexpected type '{GetPrimitiveSpecialType(type)}'");
        }
    }

    private StyledStringSegment FormatLiteral(bool value)
    {
        return new(ObjectDisplay.FormatLiteral(value), highlighter.KeywordStyle);
    }

    private StyledStringSegment FormatLiteral(string value, bool useQuotes, bool escapeNonPrintable, int numberRadix = NumberRadixDecimal)
    {
        var options = GetObjectDisplayOptions(useQuotes: useQuotes, escapeNonPrintable: escapeNonPrintable, numberRadix: numberRadix);
        return new(ObjectDisplay.FormatLiteral(value, options), stringLiteralFormat);
    }

    private StyledStringSegment FormatLiteral(char c, bool useQuotes, bool escapeNonPrintable, bool includeCodePoints = false, int numberRadix = NumberRadixDecimal)
    {
        var options = GetObjectDisplayOptions(useQuotes: useQuotes, escapeNonPrintable: escapeNonPrintable, includeCodePoints: includeCodePoints, numberRadix: numberRadix);
        return new(ObjectDisplay.FormatLiteral(c, options), stringLiteralFormat);
    }

    private StyledStringSegment FormatLiteral(sbyte value, int numberRadix = NumberRadixDecimal, CultureInfo? cultureInfo = null)
    {
        return new(ObjectDisplay.FormatLiteral(value, GetObjectDisplayOptions(numberRadix: numberRadix), cultureInfo), numericLiteralFormat);
    }

    private StyledStringSegment FormatLiteral(byte value, int numberRadix = NumberRadixDecimal, CultureInfo? cultureInfo = null)
    {
        return new(ObjectDisplay.FormatLiteral(value, GetObjectDisplayOptions(numberRadix: numberRadix), cultureInfo), numericLiteralFormat);
    }

    private StyledStringSegment FormatLiteral(short value, int numberRadix = NumberRadixDecimal, CultureInfo? cultureInfo = null)
    {
        return new(ObjectDisplay.FormatLiteral(value, GetObjectDisplayOptions(numberRadix: numberRadix), cultureInfo), numericLiteralFormat);
    }

    private StyledStringSegment FormatLiteral(ushort value, int numberRadix = NumberRadixDecimal, CultureInfo? cultureInfo = null)
    {
        return new(ObjectDisplay.FormatLiteral(value, GetObjectDisplayOptions(numberRadix: numberRadix), cultureInfo), numericLiteralFormat);
    }

    private StyledStringSegment FormatLiteral(int value, int numberRadix = NumberRadixDecimal, CultureInfo? cultureInfo = null)
    {
        return new(ObjectDisplay.FormatLiteral(value, GetObjectDisplayOptions(numberRadix: numberRadix), cultureInfo), numericLiteralFormat);
    }

    private StyledStringSegment FormatLiteral(uint value, int numberRadix = NumberRadixDecimal, CultureInfo? cultureInfo = null)
    {
        return new(ObjectDisplay.FormatLiteral(value, GetObjectDisplayOptions(numberRadix: numberRadix), cultureInfo), numericLiteralFormat);
    }

    private StyledStringSegment FormatLiteral(long value, int numberRadix = NumberRadixDecimal, CultureInfo? cultureInfo = null)
    {
        return new(ObjectDisplay.FormatLiteral(value, GetObjectDisplayOptions(numberRadix: numberRadix), cultureInfo), numericLiteralFormat);
    }

    private StyledStringSegment FormatLiteral(ulong value, int numberRadix = NumberRadixDecimal, CultureInfo? cultureInfo = null)
    {
        return new(ObjectDisplay.FormatLiteral(value, GetObjectDisplayOptions(numberRadix: numberRadix), cultureInfo), numericLiteralFormat);
    }

    private StyledStringSegment FormatLiteral(double value, CultureInfo? cultureInfo = null)
    {
        return new(ObjectDisplay.FormatLiteral(value, ObjectDisplayOptions.None, cultureInfo), numericLiteralFormat);
    }

    private StyledStringSegment FormatLiteral(float value, CultureInfo? cultureInfo = null)
    {
        return new(ObjectDisplay.FormatLiteral(value, ObjectDisplayOptions.None, cultureInfo), numericLiteralFormat);
    }

    private StyledStringSegment FormatLiteral(decimal value, CultureInfo? cultureInfo = null)
    {
        return new(ObjectDisplay.FormatLiteral(value, ObjectDisplayOptions.None, cultureInfo), numericLiteralFormat);
    }
}