using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using CSharpRepl.PrettyPromptConfig;
using CSharpRepl.Services;
using CSharpRepl.Services.Roslyn;
using NSubstitute;
using PrettyPrompt;
using Xunit;

namespace CSharpRepl.Tests;

[Collection(nameof(RoslynServices))]
public class ReadEvalPrintLoopTests : IClassFixture<RoslynServicesFixture>
{
    private readonly ReadEvalPrintLoop repl;
    private readonly FakeConsoleAbstract console;
    private readonly StringBuilder capturedOutput;
    private readonly StringBuilder capturedError;
    private readonly IPrompt prompt;
    private readonly RoslynServices services;

    public ReadEvalPrintLoopTests(RoslynServicesFixture fixture)
    {
        this.console = fixture.ConsoleStub;
        this.capturedOutput = fixture.CapturedConsoleOutput;
        this.capturedError = fixture.CapturedConsoleError;
        this.prompt = fixture.PromptStub;
        this.services = fixture.RoslynServices;
        this.repl = new ReadEvalPrintLoop(console, services, prompt);
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

        Assert.Contains("Welcome to the C# REPL", console.AnsiConsole.Output);
        Assert.Contains("Type C# at the prompt", console.AnsiConsole.Output);
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

        ((IConsoleEx)console.Received()).Clear(true);
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

        Assert.Equal("8", console.AnsiConsole.Lines.Last());
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

        Assert.Contains(@"""Hello World""", console.AnsiConsole.Output);
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

        Assert.Contains("30", console.AnsiConsole.Output);
    }

    [Fact]
    public async Task RunAsync_NugetCommand_InstallsPackage()
    {
        prompt
            .ReadLineAsync()
            .Returns(
                new PromptResult(true, "#r \"nuget: Newtonsoft.Json, 13.0.1\"", default),
                new PromptResult(true, "exit", default)
            );

        await repl.RunAsync(new Configuration());

        // use some regex wildcards to account for / ignore ansi escape sequences
        var addingReferencesMessage = Regex.Matches(
            capturedOutput.ToString(),
            "Adding references for .*'Newtonsoft.Json.13.0.1'"
        );
        var successMessage = Regex.Matches(
            capturedOutput.ToString(),
            "Package .*'Newtonsoft.Json.13.0.1'.* was successfully installed."
        );

        // assert single to make sure we only do / log package installation once.
        Assert.Single(addingReferencesMessage);
        Assert.Single(successMessage);
        Assert.Empty(capturedError.ToString());
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

        Assert.Contains("bonk!", console.AnsiConsole.Output);
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
