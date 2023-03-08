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
        var output = prettyPrinter.FormatException(exception, Level.FirstDetailed).ToString();

        Assert.Equal(expectedOutput.Replace("\n", NewLine), output);
    }

    /// <summary>
    /// https://github.com/waf/CSharpRepl/issues/193
    /// </summary>
    [Theory]
    [InlineData(Level.FirstDetailed)]
    [InlineData(Level.FirstSimple)]
    internal async Task CompilationErrorException(Level level)
    {
        var result = await services.EvaluateAsync("+");
        var exception = ((EvaluationResult.Error)result).Exception;
        var output = ToString(prettyPrinter.FormatObject(exception, level).Renderable);
        Assert.Equal("(1,2): error CS1733: Expected expression", output);
    }

    [Theory]
    [MemberData(nameof(FormatObjectInputs))]
    internal void FormatObject_ObjectInput_PrintsOutput(object obj, Level level, string expectedResult, bool expectedResultIsNotComplete)
    {
        var output = ToString(prettyPrinter.FormatObject(obj, level).Renderable);
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
        new object[] { null, Level.FirstSimple, "null", false },
        new object[] { null, Level.FirstDetailed, "null", false },

        new object[] { @"""hello world""", Level.FirstSimple, @"""\""hello world\""""", false  },
        new object[] { @"""hello world""", Level.FirstDetailed, @"""hello world""", false  },

        new object[] { "a\nb", Level.FirstSimple, @"""a\nb""", false },
        new object[] { "a\nb", Level.FirstDetailed, "a\nb", false },

        //TODO - Hubert
        //new object[] { new[] { 1, 2, 3 }, Level.FirstSimple, "int[3] { 1, 2, 3 }", false },
        //new object[] { new[] { 1, 2, 3 }, Level.FirstDetailed, $"int[3] {"{"}{NewLine}  1,{NewLine}  2,{NewLine}  3{NewLine}{"}"}{NewLine}", false },

        new object[] { typeof(int), Level.FirstSimple, "int", false },
        new object[] { typeof(int), Level.FirstDetailed, "System.Int32", true },

        new object[] { Encoding.UTF8, Level.FirstDetailed, "System.Text.UTF8Encoding.UTF8EncodingSealed", false },
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