using CSharpRepl.Services;
using CSharpRepl.Services.Roslyn;
using NSubstitute;
using PrettyPrompt;
using PrettyPrompt.Consoles;
using System;
using System.Threading.Tasks;
using Xunit;

namespace CSharpRepl.Tests
{
    [Collection(nameof(RoslynServices))]
    public class ReadEvalPrintLoopTests : IAsyncLifetime
    {
        private readonly ReadEvalPrintLoop repl;
        private readonly IConsole console;
        private readonly IPrompt prompt;
        private readonly RoslynServices services;

        public ReadEvalPrintLoopTests()
        {
            this.console = Substitute.For<IConsole>();
            this.prompt = Substitute.For<IPrompt>();
            this.services = new RoslynServices(console, new Configuration());
            this.repl = new ReadEvalPrintLoop(services, prompt, console);
        }

        public Task InitializeAsync() => services.WarmUpAsync(Array.Empty<string>());
        public Task DisposeAsync() => Task.CompletedTask;

        [Theory]
        [InlineData("help")]
        [InlineData("#help")]
        [InlineData("?")]
        public async Task RunAsync_HelpCommand_ShowsHelp(string help)
        {
            prompt
                .ReadLineAsync("> ")
                .Returns(
                    new PromptResult(true, help, false),
                    new PromptResult(true, "exit", false)
                );

            await repl.RunAsync(new Configuration());

            console.Received().WriteLine(Arg.Is<string>(str => str.Contains("Welcome to the C# REPL")));
            console.Received().WriteLine(Arg.Is<string>(str => str.Contains("Type C# at the prompt and press Enter to run it.")));
        }

        [Fact]
        public async Task RunAsync_ClearCommand_ClearsScreen()
        {
            prompt
                .ReadLineAsync("> ")
                .Returns(
                    new PromptResult(true, "clear", false),
                    new PromptResult(true, "exit", false)
                );

            await repl.RunAsync(new Configuration());

            console.Received().Clear();
        }

        [Fact]
        public async Task RunAsync_EvaluateCode_ReturnsResult()
        {
            prompt
                .ReadLineAsync("> ")
                .Returns(
                    new PromptResult(true, "5 + 3", false),
                    new PromptResult(true, "exit", false)
                );

            await repl.RunAsync(new Configuration());

            console.Received().WriteLine("8");
        }

        [Fact]
        public async Task RunAsync_LoadScript_RunsScript()
        {
            prompt
                .ReadLineAsync("> ")
                .Returns(
                    new PromptResult(true, "x", false),
                    new PromptResult(true, "exit", false)
                );

            await repl.RunAsync(new Configuration
            {
                LoadScript = @"var x = ""Hello World"";"
            });

            console.Received().WriteLine(@"""Hello World""");
        }

        [Fact]
        public async Task RunAsync_Reference_AddsReference()
        {
            prompt
                .ReadLineAsync("> ")
                .Returns(
                    new PromptResult(true, "DemoLibrary.DemoClass.Multiply(5, 6)", false),
                    new PromptResult(true, "exit", false)
                );

            await repl.RunAsync(new Configuration
            {
                References = { "Data/DemoLibrary.dll" }
            });

            console.Received().WriteLine("30");
        }

        [Fact]
        public async Task RunAsync_Exception_ShowsMessage()
        {
            prompt
                .ReadLineAsync("> ")
                .Returns(
                    new PromptResult(true, @"throw new InvalidOperationException(""bonk!"");", false),
                    new PromptResult(true, "exit", false)
                );

            await repl.RunAsync(new Configuration());

            console.Received().WriteErrorLine(Arg.Is<string>(message => message.Contains("bonk")));
        }
    }
}
