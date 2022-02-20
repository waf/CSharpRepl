using CSharpRepl.PrettyPromptConfig;
using CSharpRepl.Services;
using CSharpRepl.Services.Roslyn;
using NSubstitute;
using NSubstitute.ClearExtensions;
using PrettyPrompt;
using PrettyPrompt.Consoles;
using System.Threading.Tasks;
using Xunit;

namespace CSharpRepl.Tests;

[Collection(nameof(RoslynServices))]
public class ReadEvalPrintLoopTests : IClassFixture<RoslynServicesFixture>
{
    private readonly ReadEvalPrintLoop repl;
    private readonly IConsole console;
    private readonly IPrompt prompt;
    private readonly RoslynServices services;

    public ReadEvalPrintLoopTests(RoslynServicesFixture fixture)
    {
        this.console = fixture.ConsoleStub;
        this.prompt = fixture.PromptStub;
        this.services = fixture.RoslynServices;
        this.repl = new ReadEvalPrintLoop(services, prompt, console);

        this.console.ClearSubstitute();
        this.prompt.ClearSubstitute();
    }

    [Theory]
    [InlineData("help")]
    [InlineData("#help")]
    [InlineData("?")]
    public async Task RunAsync_HelpCommand_ShowsHelp(string help)
    {
        prompt
            .ReadLineAsync()
            .Returns(
                new PromptResult(true, help, default),
                new PromptResult(true, "exit", default)
            );

        await repl.RunAsync(new Configuration());

        console.Received().WriteLine(Arg.Is<string>(str => str.Contains("Welcome to the C# REPL")));
        console.Received().WriteLine(Arg.Is<string>(str => str.Contains("Type C# at the prompt")));
    }

    [Fact]
    public async Task RunAsync_ClearCommand_ClearsScreen()
    {
        prompt
            .ReadLineAsync()
            .Returns(
                new PromptResult(true, "clear", default),
                new PromptResult(true, "exit", default)
            );

        await repl.RunAsync(new Configuration());

        console.Received().Clear();
    }

    [Fact]
    public async Task RunAsync_EvaluateCode_ReturnsResult()
    {
        prompt
            .ReadLineAsync()
            .Returns(
                new PromptResult(true, "5 + 3", default),
                new PromptResult(true, "exit", default)
            );

        await repl.RunAsync(new Configuration());

        console.Received().WriteLine("8");
    }

    [Fact]
    public async Task RunAsync_LoadScript_RunsScript()
    {
        prompt
            .ReadLineAsync()
            .Returns(
                new PromptResult(true, "x", default),
                new PromptResult(true, "exit", default)
            );

        await repl.RunAsync(new Configuration(
            loadScript: @"var x = ""Hello World"";"
        ));

        console.Received().WriteLine(@"""Hello World""");
    }

    [Fact]
    public async Task RunAsync_Reference_AddsReference()
    {
        prompt
            .ReadLineAsync()
            .Returns(
                new PromptResult(true, "DemoLibrary.DemoClass.Multiply(5, 6)", default),
                new PromptResult(true, "exit", default)
            );

        await repl.RunAsync(new Configuration(
            references: new[] { "Data/DemoLibrary.dll" }
        ));

        console.Received().WriteLine("30");
    }

    [Fact]
    public async Task RunAsync_Exception_ShowsMessage()
    {
        prompt
            .ReadLineAsync()
            .Returns(
                new PromptResult(true, @"throw new InvalidOperationException(""bonk!"");", default),
                new PromptResult(true, "exit", default)
            );

        await repl.RunAsync(new Configuration());

        console.Received().WriteErrorLine(Arg.Is<string>(message => message.Contains("bonk")));
    }

    [Fact]
    public async Task RunAsync_ExitCommand_ExitsRepl()
    {
        prompt
            .ReadLineAsync()
            .Returns(
                new ExitApplicationKeyPress()
            );

        await repl.RunAsync(new Configuration());

        // by reaching here, the application correctly exited.
    }
}
