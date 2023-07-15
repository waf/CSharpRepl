using System;
using System.Text;
using System.Threading.Tasks;
using CSharpRepl.Services;
using CSharpRepl.Services.Roslyn;
using CSharpRepl.Services.Roslyn.Scripting;

namespace CSharpRepl;

/// <summary>
/// CSharpRepl is predominantly an interactive repl, but also supports input being piped to the executable.
/// This class handles the piped non-interactive input mode.
/// </summary>
internal sealed class PipedInputEvaluator
{
    private readonly IConsoleEx console;
    private readonly RoslynServices roslyn;

    public PipedInputEvaluator(IConsoleEx console, RoslynServices roslyn)
    {
        this.console = console;
        this.roslyn = roslyn;
    }

    /// <summary>
    /// When we're receiving pipe input, evaluate the input as it streams in.
    /// </summary>
    /// <returns>exit / error code</returns>
    public async Task<int> EvaluateStreamingPipeInputAsync()
    {
        // input could be piped forever, so don't read all the input and then evaluate it in one go.
        // instead, read the input line by line until we have a completed statement, then evaluate that.

        var statement = new StringBuilder();
        string? inputLine;
        while((inputLine = console.ReadLine()) is not null)
        {
            // batch input into a complete statement
            statement.AppendLine(inputLine);
            string input = statement.ToString();
            if (!await roslyn.IsTextCompleteStatementAsync(input))
            {
                continue;
            }
            statement.Clear();

            // evaluate complete statement.
            var result = await roslyn.EvaluateAsync(input);
            if (result is not EvaluationResult.Success)
            {
                return ErrorCode(result);
            }
        }

        return ExitCodes.Success;
    }

    /// <summary>
    /// Reads all input from stdin in one go, and evaluates it and returns.
    /// Could block forever if input never ends.
    /// </summary>
    /// <returns>exit / error code</returns>
    public async Task<int> EvaluateCollectedPipeInputAsync()
    {
        var input = new StringBuilder();
        string? line;
        while ((line = console.ReadLine()) is not null)
        {
            input.AppendLine(line);
        }

        var result = await roslyn.EvaluateAsync(input.ToString());
        return result is EvaluationResult.Success
            ? ExitCodes.Success
            : ErrorCode(result);
    }

    private int ErrorCode(EvaluationResult result)
    {
        switch (result)
        {
            case EvaluationResult.Error err:
                console.WriteErrorLine(err.Exception.Message);
                return err.Exception.HResult;
            case EvaluationResult.Cancelled:
                return ExitCodes.ErrorCancelled;
            default:
                throw new InvalidOperationException("Unhandled EvaluationResult type");
        }
    }
}
