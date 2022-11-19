using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using CSharpRepl.Services;
using CSharpRepl.Services.Roslyn;
using CSharpRepl.Services.Roslyn.Scripting;
using CSharpRepl.Services.SyntaxHighlighting;
using CSharpRepl.Services.Theming;
using Microsoft.Extensions.Caching.Memory;
using Xunit;
using static System.Environment;

namespace CSharpRepl.Tests;

public class PrettyPrinterTests : IClassFixture<RoslynServicesFixture>
{
    private readonly RoslynServices services;
    private readonly PrettyPrinter prettyPrinter;

    public PrettyPrinterTests(RoslynServicesFixture fixture)
    {
        this.services = fixture.RoslynServices;
        this.prettyPrinter = new PrettyPrinter(
            new SyntaxHighlighter(
                new MemoryCache(new MemoryCacheOptions()),
                new Theme(null, null, null, null, Array.Empty<SyntaxHighlightingColor>())),
            new Configuration());
    }

    [Theory]
    [InlineData(@"throw null;", $"System.NullReferenceException: Object reference not set to an instance of an object.")]
    [InlineData(@"void M1() => M2(); void M2() => throw null; M1()", $"System.NullReferenceException: Object reference not set to an instance of an object.\n   at void Submission#1.M2()\n   at void Submission#1.M1()")]
    [InlineData(@"async Task M1() => await M2(); async Task M2() => throw null; await M1()", $"System.NullReferenceException: Object reference not set to an instance of an object.\n   at async Task Submission#2.M2()\n   at async Task Submission#2.M1()")]
    public async Task ExceptionCallstack(string input, string expectedOutput)
    {
        var result = await services.EvaluateAsync(input);
        var exception = ((EvaluationResult.Error)result).Exception;
        var output = prettyPrinter.FormatObject(exception, displayDetails: true).Text;
        Assert.Equal(expectedOutput.Replace("\n", NewLine), output);
    }

    [Theory]
    [MemberData(nameof(FormatObjectInputs))]
    public void FormatObject_ObjectInput_PrintsOutput(object obj, bool showDetails, string expectedResult)
    {
        var prettyPrinted = prettyPrinter.FormatObject(obj, showDetails).Text;
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