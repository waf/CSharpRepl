// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Globalization;
using CSharpRepl.Services.SyntaxHighlighting;
using CSharpRepl.Services.Theming;
using Microsoft.CodeAnalysis.Classification;
using Microsoft.CodeAnalysis.Scripting.Hosting;
using Spectre.Console;

namespace Microsoft.CodeAnalysis.CSharp.Scripting.Hosting;

using static ObjectFormatterHelpers;

internal class CSharpPrimitiveFormatter : CommonPrimitiveFormatter
{
    private readonly Style numericLiteralFormat;
    private readonly Style stringLiteralFormat;
    private readonly SyntaxHighlighter highlighter;

    public CSharpPrimitiveFormatter(SyntaxHighlighter highlighter)
    {
        numericLiteralFormat = highlighter.GetStyle(ClassificationTypeNames.NumericLiteral);
        stringLiteralFormat = highlighter.GetStyle(ClassificationTypeNames.StringLiteral);
        NullLiteral = new StyledStringSegment("null", highlighter.KeywordStyle);
        this.highlighter = highlighter;
    }

    public override StyledStringSegment NullLiteral { get; }

    protected override StyledStringSegment FormatLiteral(bool value)
    {
        return new(ObjectDisplay.FormatLiteral(value), highlighter.KeywordStyle);
    }

    protected override StyledStringSegment FormatLiteral(string value, bool useQuotes, bool escapeNonPrintable, int numberRadix = NumberRadixDecimal)
    {
        var options = GetObjectDisplayOptions(useQuotes: useQuotes, escapeNonPrintable: escapeNonPrintable, numberRadix: numberRadix);
        return new(ObjectDisplay.FormatLiteral(value, options), stringLiteralFormat);
    }

    protected override StyledStringSegment FormatLiteral(char c, bool useQuotes, bool escapeNonPrintable, bool includeCodePoints = false, int numberRadix = NumberRadixDecimal)
    {
        var options = GetObjectDisplayOptions(useQuotes: useQuotes, escapeNonPrintable: escapeNonPrintable, includeCodePoints: includeCodePoints, numberRadix: numberRadix);
        return new(ObjectDisplay.FormatLiteral(c, options), stringLiteralFormat);
    }

    protected override StyledStringSegment FormatLiteral(sbyte value, int numberRadix = NumberRadixDecimal, CultureInfo? cultureInfo = null)
    {
        return new(ObjectDisplay.FormatLiteral(value, GetObjectDisplayOptions(numberRadix: numberRadix), cultureInfo), numericLiteralFormat);
    }

    protected override StyledStringSegment FormatLiteral(byte value, int numberRadix = NumberRadixDecimal, CultureInfo? cultureInfo = null)
    {
        return new(ObjectDisplay.FormatLiteral(value, GetObjectDisplayOptions(numberRadix: numberRadix), cultureInfo), numericLiteralFormat);
    }

    protected override StyledStringSegment FormatLiteral(short value, int numberRadix = NumberRadixDecimal, CultureInfo? cultureInfo = null)
    {
        return new(ObjectDisplay.FormatLiteral(value, GetObjectDisplayOptions(numberRadix: numberRadix), cultureInfo), numericLiteralFormat);
    }

    protected override StyledStringSegment FormatLiteral(ushort value, int numberRadix = NumberRadixDecimal, CultureInfo? cultureInfo = null)
    {
        return new(ObjectDisplay.FormatLiteral(value, GetObjectDisplayOptions(numberRadix: numberRadix), cultureInfo), numericLiteralFormat);
    }

    protected override StyledStringSegment FormatLiteral(int value, int numberRadix = NumberRadixDecimal, CultureInfo? cultureInfo = null)
    {
        return new(ObjectDisplay.FormatLiteral(value, GetObjectDisplayOptions(numberRadix: numberRadix), cultureInfo), numericLiteralFormat);
    }

    protected override StyledStringSegment FormatLiteral(uint value, int numberRadix = NumberRadixDecimal, CultureInfo? cultureInfo = null)
    {
        return new(ObjectDisplay.FormatLiteral(value, GetObjectDisplayOptions(numberRadix: numberRadix), cultureInfo), numericLiteralFormat);
    }

    protected override StyledStringSegment FormatLiteral(long value, int numberRadix = NumberRadixDecimal, CultureInfo? cultureInfo = null)
    {
        return new(ObjectDisplay.FormatLiteral(value, GetObjectDisplayOptions(numberRadix: numberRadix), cultureInfo), numericLiteralFormat);
    }

    protected override StyledStringSegment FormatLiteral(ulong value, int numberRadix = NumberRadixDecimal, CultureInfo? cultureInfo = null)
    {
        return new(ObjectDisplay.FormatLiteral(value, GetObjectDisplayOptions(numberRadix: numberRadix), cultureInfo), numericLiteralFormat);
    }

    protected override StyledStringSegment FormatLiteral(double value, CultureInfo? cultureInfo = null)
    {
        return new(ObjectDisplay.FormatLiteral(value, ObjectDisplayOptions.None, cultureInfo), numericLiteralFormat);
    }

    protected override StyledStringSegment FormatLiteral(float value, CultureInfo? cultureInfo = null)
    {
        return new(ObjectDisplay.FormatLiteral(value, ObjectDisplayOptions.None, cultureInfo), numericLiteralFormat);
    }

    protected override StyledStringSegment FormatLiteral(decimal value, CultureInfo? cultureInfo = null)
    {
        return new(ObjectDisplay.FormatLiteral(value, ObjectDisplayOptions.None, cultureInfo), numericLiteralFormat);
    }
}