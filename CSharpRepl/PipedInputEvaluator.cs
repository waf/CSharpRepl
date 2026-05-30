using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CSharpRepl.Services;
using CSharpRepl.Services.Roslyn;
using CSharpRepl.Services.Roslyn.Formatting;
using CSharpRepl.Services.Roslyn.Scripting;

namespace CSharpRepl;

/// <summary>
/// CSharpRepl is predominantly an interactive repl, but also supports being run non-interactively:
/// via <c>--eval</c>/<c>--eval-file</c>, or by having input piped to the executable. This class handles
/// those non-interactive modes. Unlike the interactive REPL it auto-prints the value of the final
/// expression to stdout (so callers don't need an explicit <c>Console.WriteLine</c>), and it applies
/// any command-line references/load-scripts quietly (status to stderr) so stdout stays clean.
/// </summary>
internal sealed class PipedInputEvaluator
{
    private readonly IConsoleEx console;
    private readonly RoslynServices roslyn;
    private readonly Configuration configuration;

    public PipedInputEvaluator(IConsoleEx console, RoslynServices roslyn, Configuration configuration)
    {
        this.console = console;
        this.roslyn = roslyn;
        this.configuration = configuration;
    }

    /// <summary>
    /// Evaluates a single string of C# (e.g. from --eval or --eval-file) and exits.
    /// </summary>
    /// <returns>exit / error code</returns>
    public async Task<int> EvaluateStringAsync(string input)
    {
        if (await PreloadAsync().ConfigureAwait(false) is int preloadError) return preloadError;

        var result = await roslyn.EvaluateAsync(input).ConfigureAwait(false);
        return await ProcessResultAsync(result).ConfigureAwait(false);
    }

    /// <summary>
    /// When we're receiving pipe input, evaluate the input as it streams in.
    /// </summary>
    /// <returns>exit / error code</returns>
    public async Task<int> EvaluateStreamingPipeInputAsync()
    {
        if (await PreloadAsync().ConfigureAwait(false) is int preloadError) return preloadError;

        // input could be piped forever, so don't read all the input and then evaluate it in one go.
        // instead, read the input line by line until we have a completed statement, then evaluate that.

        var statement = new StringBuilder();
        string? inputLine;
        while ((inputLine = console.ReadLine()) is not null)
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
            var exitCode = await ProcessResultAsync(result).ConfigureAwait(false);
            if (exitCode != ExitCodes.Success)
            {
                return exitCode;
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
        if (await PreloadAsync().ConfigureAwait(false) is int preloadError) return preloadError;

        var input = new StringBuilder();
        string? line;
        while ((line = console.ReadLine()) is not null)
        {
            input.AppendLine(line);
        }

        var result = await roslyn.EvaluateAsync(input.ToString());
        return await ProcessResultAsync(result).ConfigureAwait(false);
    }

    /// <summary>
    /// Applies any references (-r) and load scripts (.csx) supplied on the command line before
    /// evaluating input. Unlike the interactive REPL's preload this is quiet — there's no "Adding
    /// supplied references..." chatter and successful results are not printed — so the agent-facing
    /// stdout stays clean. Returns an error exit code if a reference/script fails to load, else null.
    /// </summary>
    private async Task<int?> PreloadAsync()
    {
        if (configuration.References.Count > 0)
        {
            var loadReferenceScript = string.Join(Environment.NewLine, configuration.References.Select(reference => $@"#r ""{reference}"""));
            var result = await roslyn.EvaluateAsync(loadReferenceScript).ConfigureAwait(false);
            if (result is not EvaluationResult.Success) return await ProcessResultAsync(result).ConfigureAwait(false);
        }

        if (configuration.LoadScript is not null)
        {
            var result = await roslyn.EvaluateAsync(configuration.LoadScript, configuration.LoadScriptArgs).ConfigureAwait(false);
            if (result is not EvaluationResult.Success) return await ProcessResultAsync(result).ConfigureAwait(false);
        }

        return null;
    }

    /// <summary>
    /// Prints a successful result's value to stdout or writes an error message to stderr, and returns
    /// the corresponding process exit code. The value is written as plain text (no Spectre rendering or
    /// ANSI color) on the standard output stream — deterministic and suitable for capture/piping.
    /// </summary>
    private async Task<int> ProcessResultAsync(EvaluationResult result)
    {
        switch (result)
        {
            case EvaluationResult.Success success:
                if (success.ReturnValue.HasValue)
                {
                    var text = await roslyn.PrettyPrintToStringAsync(success.ReturnValue.Value, Level.FirstSimple).ConfigureAwait(false);
                    console.WriteStandardOutputLine(text);
                }
                return ExitCodes.Success;
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
