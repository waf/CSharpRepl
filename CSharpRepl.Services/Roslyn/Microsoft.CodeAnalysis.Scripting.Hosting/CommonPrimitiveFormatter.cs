// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Globalization;
using System.Reflection;
using CSharpRepl.Services.Theming;
using Microsoft.CodeAnalysis;

namespace Microsoft.CodeAnalysis.Scripting.Hosting;

using static ObjectFormatterHelpers;

internal abstract partial class CommonPrimitiveFormatter
{
    /// <summary>
    /// String that describes "null" literal in the language.
    /// </summary>
    public abstract StyledStringSegment NullLiteral { get; }

    protected abstract StyledStringSegment FormatLiteral(bool value);
    protected abstract StyledStringSegment FormatLiteral(string value, bool quote, bool escapeNonPrintable, int numberRadix = NumberRadixDecimal);
    protected abstract StyledStringSegment FormatLiteral(char value, bool quote, bool escapeNonPrintable, bool includeCodePoints = false, int numberRadix = NumberRadixDecimal);
    protected abstract StyledStringSegment FormatLiteral(sbyte value, int numberRadix = NumberRadixDecimal, CultureInfo? cultureInfo = null);
    protected abstract StyledStringSegment FormatLiteral(byte value, int numberRadix = NumberRadixDecimal, CultureInfo? cultureInfo = null);
    protected abstract StyledStringSegment FormatLiteral(short value, int numberRadix = NumberRadixDecimal, CultureInfo? cultureInfo = null);
    protected abstract StyledStringSegment FormatLiteral(ushort value, int numberRadix = NumberRadixDecimal, CultureInfo? cultureInfo = null);
    protected abstract StyledStringSegment FormatLiteral(int value, int numberRadix = NumberRadixDecimal, CultureInfo? cultureInfo = null);
    protected abstract StyledStringSegment FormatLiteral(uint value, int numberRadix = NumberRadixDecimal, CultureInfo? cultureInfo = null);
    protected abstract StyledStringSegment FormatLiteral(long value, int numberRadix = NumberRadixDecimal, CultureInfo? cultureInfo = null);
    protected abstract StyledStringSegment FormatLiteral(ulong value, int numberRadix = NumberRadixDecimal, CultureInfo? cultureInfo = null);
    protected abstract StyledStringSegment FormatLiteral(double value, CultureInfo? cultureInfo = null);
    protected abstract StyledStringSegment FormatLiteral(float value, CultureInfo? cultureInfo = null);
    protected abstract StyledStringSegment FormatLiteral(decimal value, CultureInfo? cultureInfo = null);

    /// <summary>
    /// Returns null if the type is not considered primitive in the target language.
    /// </summary>
    public StyledStringSegment? FormatPrimitive(object? obj, CommonPrimitiveFormatterOptions options)
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
}