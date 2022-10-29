// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Globalization;
using CSharpRepl.Services.SyntaxHighlighting;
using Microsoft.CodeAnalysis.Classification;
using Microsoft.CodeAnalysis.Scripting.Hosting;
using PrettyPrompt.Highlighting;

namespace Microsoft.CodeAnalysis.CSharp.Scripting.Hosting;

using static ObjectFormatterHelpers;

internal class CSharpPrimitiveFormatter : CommonPrimitiveFormatter
{
    private readonly ConsoleFormat numericLiteralFormat;
    private readonly ConsoleFormat stringLiteralFormat;
    private readonly ConsoleFormat keywordFormat;

    public CSharpPrimitiveFormatter(SyntaxHighlighter syntaxHighlighter)
    {
        numericLiteralFormat = new ConsoleFormat(Foreground: syntaxHighlighter.GetColor(ClassificationTypeNames.NumericLiteral));
        stringLiteralFormat = new ConsoleFormat(Foreground: syntaxHighlighter.GetColor(ClassificationTypeNames.StringLiteral));
        keywordFormat = new ConsoleFormat(Foreground: syntaxHighlighter.GetColor(ClassificationTypeNames.Keyword));
        NullLiteral = new FormattedString("null", keywordFormat);
    }

    protected override FormattedString NullLiteral { get; }

    protected override FormattedString FormatLiteral(bool value)
    {
        return new(ObjectDisplay.FormatLiteral(value), keywordFormat);
    }

    protected override FormattedString FormatLiteral(string value, bool useQuotes, bool escapeNonPrintable, int numberRadix = NumberRadixDecimal)
    {
        var options = GetObjectDisplayOptions(useQuotes: useQuotes, escapeNonPrintable: escapeNonPrintable, numberRadix: numberRadix);
        return new(ObjectDisplay.FormatLiteral(value, options), stringLiteralFormat);
    }

    protected override FormattedString FormatLiteral(char c, bool useQuotes, bool escapeNonPrintable, bool includeCodePoints = false, int numberRadix = NumberRadixDecimal)
    {
        var options = GetObjectDisplayOptions(useQuotes: useQuotes, escapeNonPrintable: escapeNonPrintable, includeCodePoints: includeCodePoints, numberRadix: numberRadix);
        return new(ObjectDisplay.FormatLiteral(c, options), stringLiteralFormat);
    }

    protected override FormattedString FormatLiteral(sbyte value, int numberRadix = NumberRadixDecimal, CultureInfo? cultureInfo = null)
    {
        return new(ObjectDisplay.FormatLiteral(value, GetObjectDisplayOptions(numberRadix: numberRadix), cultureInfo), numericLiteralFormat);
    }

    protected override FormattedString FormatLiteral(byte value, int numberRadix = NumberRadixDecimal, CultureInfo? cultureInfo = null)
    {
        return new(ObjectDisplay.FormatLiteral(value, GetObjectDisplayOptions(numberRadix: numberRadix), cultureInfo), numericLiteralFormat);
    }

    protected override FormattedString FormatLiteral(short value, int numberRadix = NumberRadixDecimal, CultureInfo? cultureInfo = null)
    {
        return new(ObjectDisplay.FormatLiteral(value, GetObjectDisplayOptions(numberRadix: numberRadix), cultureInfo), numericLiteralFormat);
    }

    protected override FormattedString FormatLiteral(ushort value, int numberRadix = NumberRadixDecimal, CultureInfo? cultureInfo = null)
    {
        return new(ObjectDisplay.FormatLiteral(value, GetObjectDisplayOptions(numberRadix: numberRadix), cultureInfo), numericLiteralFormat);
    }

    protected override FormattedString FormatLiteral(int value, int numberRadix = NumberRadixDecimal, CultureInfo? cultureInfo = null)
    {
        return new(ObjectDisplay.FormatLiteral(value, GetObjectDisplayOptions(numberRadix: numberRadix), cultureInfo), numericLiteralFormat);
    }

    protected override FormattedString FormatLiteral(uint value, int numberRadix = NumberRadixDecimal, CultureInfo? cultureInfo = null)
    {
        return new(ObjectDisplay.FormatLiteral(value, GetObjectDisplayOptions(numberRadix: numberRadix), cultureInfo), numericLiteralFormat);
    }

    protected override FormattedString FormatLiteral(long value, int numberRadix = NumberRadixDecimal, CultureInfo? cultureInfo = null)
    {
        return new(ObjectDisplay.FormatLiteral(value, GetObjectDisplayOptions(numberRadix: numberRadix), cultureInfo), numericLiteralFormat);
    }

    protected override FormattedString FormatLiteral(ulong value, int numberRadix = NumberRadixDecimal, CultureInfo? cultureInfo = null)
    {
        return new(ObjectDisplay.FormatLiteral(value, GetObjectDisplayOptions(numberRadix: numberRadix), cultureInfo), numericLiteralFormat);
    }

    protected override FormattedString FormatLiteral(double value, CultureInfo? cultureInfo = null)
    {
        return new(ObjectDisplay.FormatLiteral(value, ObjectDisplayOptions.None, cultureInfo), numericLiteralFormat);
    }

    protected override FormattedString FormatLiteral(float value, CultureInfo? cultureInfo = null)
    {
        return new(ObjectDisplay.FormatLiteral(value, ObjectDisplayOptions.None, cultureInfo), numericLiteralFormat);
    }

    protected override FormattedString FormatLiteral(decimal value, CultureInfo? cultureInfo = null)
    {
        return new(ObjectDisplay.FormatLiteral(value, ObjectDisplayOptions.None, cultureInfo), numericLiteralFormat);
    }
}