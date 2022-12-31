using System;
using System.IO;
using System.Threading.Tasks;
using CSharpRepl.Services;
using CSharpRepl.Services.Roslyn;
using CSharpRepl.Services.Roslyn.Scripting;
using Microsoft.CodeAnalysis;
using NSubstitute;
using Xunit;

namespace CSharpRepl.Tests;

[Collection(nameof(RoslynServices))]
public class DisassemblerTests : IAsyncLifetime
{
    private readonly RoslynServices services;

    public DisassemblerTests()
    {
        var console = Substitute.For<IConsoleEx>();
        console.BufferWidth.Returns(200);
        this.services = new RoslynServices(console, new Configuration(), new TestTraceLogger());
    }

    public Task InitializeAsync() => services.WarmUpAsync(Array.Empty<string>());
    public Task DisposeAsync() => Task.CompletedTask;

    [Theory]
    [InlineData(OptimizationLevel.Debug, "TopLevelProgram")]
    [InlineData(OptimizationLevel.Release, "TopLevelProgram")]
    [InlineData(OptimizationLevel.Debug, "TypeDeclaration")]
    [InlineData(OptimizationLevel.Release, "TypeDeclaration")]
    public void Disassemble_InputCSharp_OutputIL(OptimizationLevel optimizationLevel, string testCase)
    {
        var input = File.ReadAllText($"./Data/Disassembly/{testCase}.Input.txt").Replace("\r\n", "\n");
        var expectedOutput = File.ReadAllText($"./Data/Disassembly/{testCase}.Output.{optimizationLevel}.il").Replace("\r\n", "\n");

        var result = services.ConvertToIntermediateLanguage(input, debugMode: optimizationLevel == OptimizationLevel.Debug).Result;
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
        await services.EvaluateAsync("using System.Globalization;");

        // disassemble code that uses the above imported namespace.
        var result = await services.ConvertToIntermediateLanguage("var x = CultureInfo.CurrentCulture;", debugMode: false);

        var success = Assert.IsType<EvaluationResult.Success>(result);
        Assert.Contains("Compiling code as Console Application (with top-level statements): succeeded", success.ReturnValue.ToString());
    }

    [Fact]
    public async Task Disassemble_InputAcrossMultipleReplLines_CanDisassemble()
    {
        // define a variable
        await services.EvaluateAsync("var x = 5;");

        // disassemble code that uses the above variable. This is an interesting case as the roslyn scripting will convert
        // the above local variable into a field, so it can be referenced by a subsequent script.
        var result = await services.ConvertToIntermediateLanguage("Console.WriteLine(x)", debugMode: false);

        var success = Assert.IsType<EvaluationResult.Success>(result);
        Assert.Contains("Compiling code as Scripting session (will be overly verbose): succeeded", success.ReturnValue.ToString());
    }
}
