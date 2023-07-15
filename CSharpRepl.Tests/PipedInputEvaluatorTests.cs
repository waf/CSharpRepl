using CSharpRepl.Services.Roslyn;
using NSubstitute;
using System.Threading.Tasks;
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
        this.pipedInputEvaluator = new PipedInputEvaluator(console, roslyn);
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
