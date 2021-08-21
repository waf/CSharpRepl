using CSharpRepl.Services;
using CSharpRepl.Services.Disassembly;
using CSharpRepl.Services.Roslyn;
using CSharpRepl.Services.Roslyn.References;
using CSharpRepl.Services.Roslyn.Scripting;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using NSubstitute;
using PrettyPrompt.Consoles;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Xunit;

namespace CSharpRepl.Tests
{
    [Collection(nameof(RoslynServices))]
    public class DisassemblerTests : IAsyncLifetime
    {
        private readonly Disassembler disassembler;
        private readonly RoslynServices services;

        public DisassemblerTests()
        {
            var options = new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                usings: Array.Empty<string>()
            );
            var console = Substitute.For<IConsole>();
            console.BufferWidth.Returns(200);
            var referenceService = new AssemblyReferenceService(new Configuration());
            var scriptRunner = new ScriptRunner(console, options, referenceService);

            this.disassembler = new Disassembler(options, referenceService, scriptRunner);
            this.services = new RoslynServices(console, new Configuration());
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
            var input = File.ReadAllText($"./Data/Disassembly/{testCase}.Input.cs").Replace("\r\n", "\n");
            var expectedOutput = File.ReadAllText($"./Data/Disassembly/{testCase}.Output.{optimizationLevel}.il").Replace("\r\n", "\n");

            var result = disassembler.Disassemble(input, debugMode: optimizationLevel == OptimizationLevel.Debug);
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
            var result = await services.ConvertToSyntaxHighlightedIntermediateLanguage("var x = CultureInfo.CurrentCulture;", debugMode: false);

            Assert.Contains("Compiling code as Console Application (with top-level statements): succeeded", result);
        }

        [Fact]
        public async Task Disassemble_InputAcrossMultipleReplLines_CanDisassemble()
        {
            // define a variable
            await services.EvaluateAsync("var x = 5;");

            // disassemble code that uses the above variable. This is an interesting case as the roslyn scripting will convert
            // the above local variable into a field, so it can be referenced by a subsequent script.
            var result = await services.ConvertToSyntaxHighlightedIntermediateLanguage("Console.WriteLine(x)", debugMode: false);

            Assert.Contains("Compiling code as Scripting session (will be overly verbose): succeeded", result);
        }
    }
}
