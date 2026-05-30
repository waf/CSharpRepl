using System.Threading.Tasks;
using CSharpRepl.Services;
using CSharpRepl.Services.Roslyn;
using NSubstitute;
using Xunit;

namespace CSharpRepl.Tests;

[Collection(nameof(RoslynServices))]
public class PipedInputEvaluatorTests : IClassFixture<RoslynServicesFixture>
{
    private readonly FakeConsoleAbstract console;
    private readonly RoslynServices roslyn;
    private readonly PipedInputEvaluator pipedInputEvaluator;

    public PipedInputEvaluatorTests(RoslynServicesFixture fixture)
    {
        this.console = fixture.ConsoleStub;
        this.roslyn = fixture.RoslynServices;
        this.pipedInputEvaluator = new PipedInputEvaluator(console, roslyn, new Configuration());
    }

    [Fact]
    public async Task EvaluateCollectedPipeInputAsync_InputThrows_ReturnsErrorCodeAndWritesMessage()
    {
        // Use a fresh console so toggling IsErrorRedirected / the ReadLine stub doesn't leak into
        // the other tests sharing the RoslynServices fixture collection.
        var (errorConsole, _, stderr) = FakeConsole.CreateStubbedOutputAndError();
        errorConsole.PrettyPromptConsole.IsErrorRedirected = true; // route WriteErrorLine to the captured error buffer
        errorConsole.ReadLine().Returns(
            @"throw new System.Exception(""boom"");",
            (string)null // end of piped input (cast so NSubstitute treats it as a value, not a null params array)
        );
        var evaluator = new PipedInputEvaluator(errorConsole, roslyn, new Configuration());

        var result = await evaluator.EvaluateCollectedPipeInputAsync();

        Assert.NotEqual(ExitCodes.Success, result);
        Assert.Contains("boom", stderr.ToString());
    }

    [Fact]
    public async Task EvaluateStreamingPipeInputAsync_InputThrows_ReturnsErrorCodeAndWritesMessage()
    {
        var (errorConsole, _, stderr) = FakeConsole.CreateStubbedOutputAndError();
        errorConsole.PrettyPromptConsole.IsErrorRedirected = true;
        errorConsole.ReadLine().Returns(
            @"throw new System.Exception(""kaboom"");",
            (string)null // end of piped input (cast so NSubstitute treats it as a value, not a null params array)
        );
        var evaluator = new PipedInputEvaluator(errorConsole, roslyn, new Configuration());

        var result = await evaluator.EvaluateStreamingPipeInputAsync();

        Assert.NotEqual(ExitCodes.Success, result);
        Assert.Contains("kaboom", stderr.ToString());
    }

    [Fact]
    public async Task EvaluateCollectedPipeInputAsync_FullyCollectsInput_ThenEvaluatesInput()
    {
        // verify we're collecting the input entirely before evaluating it. If we evaluated it line by line,
        // the following program would have an error because the if(false) { ... } would be evaluated as a
        // complete statement, and the "else" would be a syntax error.
        console.ReadLine().Returns(
            "if(false)",
            "{",
            "    Console.WriteLine(\"true\");",
            "}",
            "else",
            "{",
            "    Console.WriteLine(\"false\");",
            "}",
            null // end of piped input
        );
        var result = await this.pipedInputEvaluator.EvaluateCollectedPipeInputAsync();

        Assert.Equal(ExitCodes.Success, result);
    }

    [Fact]
    public async Task EvaluateCollectedPipeInputAsync_ValueReturningExpression_AutoPrintsResult()
    {
        // Non-interactive mode auto-prints the value of the final expression as plain text, so callers
        // don't need an explicit Console.WriteLine.
        var (freshConsole, stdout) = FakeConsole.CreateStubbedOutput();
        freshConsole.ReadLine().Returns("1 + 1", (string)null);
        var evaluator = new PipedInputEvaluator(freshConsole, roslyn, new Configuration());

        var result = await evaluator.EvaluateCollectedPipeInputAsync();

        Assert.Equal(ExitCodes.Success, result);
        Assert.Equal("2", stdout.ToString().TrimEnd());
    }

    [Fact]
    public async Task EvaluateStringAsync_ValueReturningExpression_AutoPrintsResult()
    {
        // The --eval / --eval-file entry point: evaluate a single string and auto-print its result.
        var (freshConsole, stdout) = FakeConsole.CreateStubbedOutput();
        var evaluator = new PipedInputEvaluator(freshConsole, roslyn, new Configuration());

        var result = await evaluator.EvaluateStringAsync("40 + 2");

        Assert.Equal(ExitCodes.Success, result);
        Assert.Equal("42", stdout.ToString().TrimEnd());
    }

    [Fact]
    public async Task EvaluateStreamingPipeInputAsync_StreamsInput_EvaluatesCompleteStatements()
    {
        // in this mode, we want to read input line by line, group them into complete statements, and then
        // evaluate the complete statement. If we just did line-by-line then the below input would cause a
        // syntax error on the standalone `if(true)`
        console.ReadLine().Returns(
            "if(true)",
            "{",
            "    Console.WriteLine(\"true\");",
            "}",
            "Console.WriteLine(\"false\");",
            null // end of piped input
        );
        var result = await this.pipedInputEvaluator.EvaluateStreamingPipeInputAsync();

        Assert.Equal(ExitCodes.Success, result);
    }
}
