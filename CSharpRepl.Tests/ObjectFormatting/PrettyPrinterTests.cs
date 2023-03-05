using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CSharpRepl.Services;
using CSharpRepl.Services.Roslyn;
using CSharpRepl.Services.Roslyn.Scripting;
using CSharpRepl.Services.SyntaxHighlighting;
using CSharpRepl.Services.Theming;
using Microsoft.Extensions.Caching.Memory;
using Spectre.Console;
using Spectre.Console.Rendering;
using Spectre.Console.Testing;
using Xunit;
using static System.Environment;

namespace CSharpRepl.Tests.ObjectFormatting;

public class PrettyPrinterTests : IClassFixture<RoslynServicesFixture>
{
    private readonly FakeConsoleAbstract console;
    private readonly RoslynServices services;
    private readonly PrettyPrinter prettyPrinter;

    public PrettyPrinterTests(RoslynServicesFixture fixture)
    {
        console = fixture.ConsoleStub;
        services = fixture.RoslynServices;
        prettyPrinter = new PrettyPrinter(
            new SyntaxHighlighter(
                new MemoryCache(new MemoryCacheOptions()),
                new Theme(null, null, null, null, Array.Empty<SyntaxHighlightingColor>())),
            new Configuration());
    }

    [Theory]
    [InlineData(@"throw null;", $"NullReferenceException: Object reference not set to an instance of an object.")]
    [InlineData(@"void M1() => M2(); void M2() => throw null; M1()", $"NullReferenceException: Object reference not set to an instance of an object.\n    at void M2()\n    at void M1()")]
    [InlineData(@"async Task M1() => await M2(); async Task M2() => throw null; await M1()", $"NullReferenceException: Object reference not set to an instance of an object.\n    at async Task M2()\n    at async Task M1()")]
    [InlineData(@"void M((int A, int B) tuple) => throw null; M(default)", $"NullReferenceException: Object reference not set to an instance of an object.\n    at void M((int A, int B) tuple)")]
    public async Task ExceptionCallstack(string input, string expectedOutput)
    {
        var result = await services.EvaluateAsync(input);
        var exception = ((EvaluationResult.Error)result).Exception;
        var output = prettyPrinter.FormatException(exception, detailed: true).ToString();

        Assert.Equal(expectedOutput.Replace("\n", NewLine), output);
    }

    /// <summary>
    /// https://github.com/waf/CSharpRepl/issues/193
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task CompilationErrorException(bool detailedOutput)
    {
        var result = await services.EvaluateAsync("+");
        var exception = ((EvaluationResult.Error)result).Exception;
        var output = ToString(prettyPrinter.FormatObject(exception, detailedOutput).Renderable);
        Assert.Equal("(1,2): error CS1733: Expected expression", output);
    }

    [Theory]
    [MemberData(nameof(FormatObjectInputs))]
    public void FormatObject_ObjectInput_PrintsOutput(object obj, bool showDetails, string expectedResult, bool expectedResultIsNotComplete)
    {
        var output = ToString(prettyPrinter.FormatObject(obj, showDetails).Renderable);
        if (expectedResultIsNotComplete)
        {
            Assert.StartsWith(expectedResult, output);
        }
        else
        {
            Assert.Equal(expectedResult, output);
        }
    }

    public static IEnumerable<object[]> FormatObjectInputs => new[]
    {
        new object[] { null, false, "null", false },
        new object[] { null, true, "null", false },

        new object[] { @"""hello world""", false, @"""\""hello world\""""", false  },
        new object[] { @"""hello world""", true, @"""hello world""", false  },

        new object[] { "a\nb", false, @"""a\nb""", false },
        new object[] { "a\nb", true, "a\nb", false },

        //TODO - Hubert
        //new object[] { new[] { 1, 2, 3 }, false, "int[3] { 1, 2, 3 }", false },
        //new object[] { new[] { 1, 2, 3 }, true, $"int[3] {"{"}{NewLine}  1,{NewLine}  2,{NewLine}  3{NewLine}{"}"}{NewLine}", false },

        new object[] { typeof(int), false, "int", false },
        new object[] { typeof(int), true, "System.Int32", true },

        new object[] { Encoding.UTF8, true, "System.Text.UTF8Encoding.UTF8EncodingSealed", false },
    };

    private static string ToString(IRenderable renderable)
    {
        const int Width = 1000;
        var options = new RenderOptions(new TestCapabilities(), new Size(Width, 1000));
        var sb = new StringBuilder();
        foreach (var segment in renderable.Render(options, Width))
        {
            sb.Append(segment.Text);
        }
        return sb.ToString();
    }
}