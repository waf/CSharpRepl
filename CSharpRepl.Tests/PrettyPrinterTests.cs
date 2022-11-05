using System;
using System.Collections.Generic;
using System.Text;
using CSharpRepl.Services;
using CSharpRepl.Services.Roslyn;
using CSharpRepl.Services.SyntaxHighlighting;
using CSharpRepl.Services.Theming;
using Microsoft.Extensions.Caching.Memory;
using Xunit;
using static System.Environment;

namespace CSharpRepl.Tests;

public class PrettyPrinterTests
{
    [Theory]
    [MemberData(nameof(FormatObjectInputs))]
    public void FormatObject_ObjectInput_PrintsOutput(object obj, bool showDetails, string expectedResult)
    {
        var prettyPrinted = new PrettyPrinter(
            new SyntaxHighlighter(
                new MemoryCache(new MemoryCacheOptions()), 
                new Theme(null, null, null, null, Array.Empty<SyntaxHighlightingColor>())),
            new Configuration())
            .FormatObject(obj, showDetails)
            .Text;
        Assert.Equal(expectedResult, prettyPrinted);
    }

    public static IEnumerable<object[]> FormatObjectInputs = new[]
    {
        new object[] { null, false, "null" },
        new object[] { null, true, "null" },

        new object[] { @"""hello world""", false, @"""\""hello world\"""""},
        new object[] { @"""hello world""", true, @"""hello world"""},

        new object[] { "a\nb", false, @"""a\nb"""},
        new object[] { "a\nb", true, "a\nb"},

        new object[] { new[] { 1, 2, 3 }, false, "int[3] { 1, 2, 3 }"},
        new object[] { new[] { 1, 2, 3 }, true, $"int[3] {"{"}{NewLine}  1,{NewLine}  2,{NewLine}  3{NewLine}{"}"}{NewLine}"},

        new object[] { Encoding.UTF8, true, "System.Text.UTF8Encoding+UTF8EncodingSealed"},
    };
}