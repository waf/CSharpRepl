using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CSharpRepl.Services;
using CSharpRepl.Services.Roslyn;
using CSharpRepl.Services.Roslyn.Scripting;
using Microsoft.CodeAnalysis;
using NSubstitute;
using PrettyPrompt.Highlighting;
using Xunit;

namespace CSharpRepl.Tests;

[Collection(nameof(RoslynServices))]
public class DisassemblerTests : IAsyncLifetime
{
    private readonly RoslynServices services;

    public DisassemblerTests()
    {
        var console = FakeConsole.Create(width: 200);
        this.services = new RoslynServices(console, new Configuration(), new TestTraceLogger());
    }

    public ValueTask InitializeAsync() => new(services.WarmUpAsync([]));
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Theory]
    [InlineData(OptimizationLevel.Debug, "TopLevelProgram")]
    [InlineData(OptimizationLevel.Release, "TopLevelProgram")]
    [InlineData(OptimizationLevel.Debug, "TypeDeclaration")]
    [InlineData(OptimizationLevel.Release, "TypeDeclaration")]
    public async Task Disassemble_InputCSharp_OutputILAsync(OptimizationLevel optimizationLevel, string testCase)
    {
        var input = File.ReadAllText($"./Data/Disassembly/{testCase}.Input.txt").Replace("\r\n", "\n");
        var expectedOutput = File.ReadAllText($"./Data/Disassembly/{testCase}.Output.{optimizationLevel}.il").Replace("\r\n", "\n").Trim();

        var result = await services.ConvertToIntermediateLanguage(input, debugMode: optimizationLevel == OptimizationLevel.Debug);
        var actualOutput = Assert
            .IsType<EvaluationResult.Success>(result)
            .ReturnValue
            .ToString();

        Assert.Equal(expectedOutput, actualOutput);
    }

    [Fact]
    public async Task Disassemble_ImportsAcrossMultipleReplLines_CanDisassemble()
    {
        // import a namespace
        await services.EvaluateAsync("using System.Globalization;", cancellationToken: TestContext.Current.CancellationToken);

        // disassemble code that uses the above imported namespace.
        var result = await services.ConvertToIntermediateLanguage("var x = CultureInfo.CurrentCulture;", debugMode: false);

        var success = Assert.IsType<EvaluationResult.Success>(result);
        Assert.Contains("Compiling code as Console Application (with top-level statements): succeeded", success.ReturnValue.ToString());
    }

    [Fact]
    public async Task Disassemble_InputAcrossMultipleReplLines_CanDisassemble()
    {
        // define a variable
        await services.EvaluateAsync("var x = 5;", cancellationToken: TestContext.Current.CancellationToken);

        // disassemble code that uses the above variable. This is an interesting case as the roslyn scripting will convert
        // the above local variable into a field, so it can be referenced by a subsequent script.
        var result = await services.ConvertToIntermediateLanguage("Console.WriteLine(x)", debugMode: false);

        var success = Assert.IsType<EvaluationResult.Success>(result);
        Assert.Contains("Compiling code as Scripting session (will be overly verbose): succeeded", success.ReturnValue.ToString());
    }

    [Fact]
    public async Task Disassemble_AnnotatesILWithSourceLines()
    {
        // the IL view interleaves each C# statement (as a comment) above the IL it compiled to.
        var result = await services.ConvertToIntermediateLanguage("Console.WriteLine(42);", debugMode: true);

        var success = Assert.IsType<EvaluationResult.Success>(result);
        var output = success.ReturnValue.ToString();

        // the source line appears as a comment...
        Assert.Contains("// Console.WriteLine(42);", output);
        // ...immediately above the IL it produced, and the raw sequence-point markers are gone.
        Assert.DoesNotContain("// sequence point:", output);
    }

    [Fact]
    public async Task Disassemble_ProducesValidHighlightSpans()
    {
        var result = await services.ConvertToIntermediateLanguage("var x = 5;", debugMode: true);

        var success = Assert.IsType<EvaluationResult.Success>(result);
        var formatted = Assert.IsType<FormattedString>(success.ReturnValue.Value);
        var text = formatted.Text!;
        var spans = formatted.FormatSpans.ToArray();

        Assert.NotEmpty(spans);

        // every span must be in-bounds...
        Assert.All(spans, s => Assert.True(s.Start >= 0 && s.Start + s.Length <= text.Length));

        // ...and non-overlapping when sorted, which the ANSI renderer requires.
        var sorted = spans.OrderBy(s => s.Start).ToArray();
        for (var i = 1; i < sorted.Length; i++)
        {
            Assert.True(sorted[i - 1].Start + sorted[i - 1].Length <= sorted[i].Start, "highlight spans must not overlap");
        }

        // the IL opcode is highlighted...
        Assert.Contains(spans, s => s.Start == text.IndexOf("ldc.i4.5"));
        // ...and the C# `var` keyword inside the "// var x = 5;" comment is highlighted at the right offset,
        // proving the embedded C# is classified and offset correctly.
        var varInComment = text.IndexOf("// var x = 5;") + "// ".Length;
        Assert.Contains(spans, s => s.Start == varInComment && s.Length == "var".Length);
    }

    /// <summary>
    /// https://github.com/waf/CSharpRepl/issues/208
    /// </summary>
    [Fact]
    public async Task Disassemble_Empty()
    {
        // import a namespace
        await services.EvaluateAsync("using System;", cancellationToken: TestContext.Current.CancellationToken);

        // disassemble code that uses the above imported namespace.
        var result = await services.ConvertToIntermediateLanguage("", debugMode: false);

        var success = Assert.IsType<EvaluationResult.Success>(result);
        var resultText = success.ReturnValue.ToString();
        Assert.Contains(".class private auto ansi '<Module>'", resultText);
        Assert.Contains("// end of class <Module>", resultText);
        Assert.Contains("// Disassembled in Release Mode", resultText);
    }
}
