// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using CSharpRepl.InjectedHook.Contracts;
using CSharpRepl.Repls.Common;
using CSharpRepl.Services;
using CSharpRepl.Services.Remote;
using CSharpRepl.Services.Remote.Commands;
using CSharpRepl.Services.Roslyn;
using CSharpRepl.Services.Roslyn.Formatting;

namespace CSharpRepl.Repls;

/// <summary>
/// Non-interactive inspect evaluation: the remote twin of <see cref="PipedInputEvaluator"/> (and the headless
/// counterpart to <see cref="RemoteReadEvalPrintLoop"/>), for <c>inspect &lt;pid&gt; --eval</c>/<c>--eval-file</c>
/// and piped stdin. Auto-prints the final value as plain text on stdout (errors to stderr, nonzero exit) and
/// honors inspect commands via the same <see cref="InspectorCommandProcessor"/> as the interactive loop.
/// </summary>
internal sealed class RemotePipedInputEvaluator
{
    private const int EvaluationErrorExitCode = 1;

    private readonly IConsoleService console;
    private readonly RemoteSession session;
    private readonly RoslynServices roslyn;
    private readonly InspectorCommandProcessor commands;

    public RemotePipedInputEvaluator(IConsoleService console, RemoteSession session, RoslynServices roslyn)
    {
        this.console = console;
        this.session = session;
        this.roslyn = roslyn;
        this.commands = new InspectorCommandProcessor(session);
    }

    /// <summary>Evaluates a single submission (e.g. from --eval or --eval-file) in the target and exits.</summary>
    public Task<int> EvaluateStringAsync(string input) => EvaluateSubmissionAsync(input, CancellationToken.None);

    /// <summary>Reads all of stdin and evaluates it as a single submission. Could block forever if input never ends.</summary>
    public Task<int> EvaluateCollectedPipeInputAsync() =>
        EvaluateSubmissionAsync(PipedInputReader.ReadAll(console), CancellationToken.None);

    /// <summary>
    /// Evaluates piped stdin as it streams in, batching lines into complete statements (via the local Roslyn's
    /// syntactic check, which needs no target references) and sending each batch to the target. Commands are
    /// handled before the completeness gate, which would never accept e.g. "#patches" as complete.
    /// </summary>
    public Task<int> EvaluateStreamingPipeInputAsync() =>
        PipedInputReader.StreamAsync(
            console,
            isComplete: roslyn.IsTextCompleteStatementAsync,
            evaluate: text => EvaluateAsync(text, CancellationToken.None),
            intercept: text => TryRunCommandAsync(text, CancellationToken.None));

    /// <summary>Runs a submission as a command if it is one, otherwise evaluates it.</summary>
    private async Task<int> EvaluateSubmissionAsync(string input, CancellationToken cancellationToken)
    {
        var commandResult = await TryRunCommandAsync(input.Trim(), cancellationToken).ConfigureAwait(false);
        if (commandResult.Handled) return commandResult.ExitCode;

        return await EvaluateAsync(input, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Runs <paramref name="commandText"/> as an inspect command. Returns Handled=false (no I/O) when
    /// it isn't a command, so the caller falls through to evaluation.</summary>
    private async Task<(bool Handled, int ExitCode)> TryRunCommandAsync(string commandText, CancellationToken cancellationToken)
    {
        InspectorCommandResult? result;
        try
        {
            result = await commands.TryExecuteAsync(commandText, cancellationToken).ConfigureAwait(false);
        }
        catch (IOException ex)
        {
            console.WriteStandardErrorLine($"Lost the connection to the target process: {ex.Message}");
            return (true, EvaluationErrorExitCode);
        }

        if (result is null) return (false, ExitCodes.Success);

        var failed = false;
        InspectorCommandResultPrinter.Print(
            result,
            console.WriteStandardOutputLine,
            message => { failed = true; console.WriteStandardErrorLine(message); });
        return (true, failed ? EvaluationErrorExitCode : ExitCodes.Success);
    }

    private async Task<int> EvaluateAsync(string code, CancellationToken cancellationToken)
    {
        EvalResponse result;
        try
        {
            result = await session.EvalAsync(code, detailed: false, cancellationToken).ConfigureAwait(false);
        }
        catch (IOException ex)
        {
            console.WriteStandardErrorLine($"Lost the connection to the target process: {ex.Message}");
            return EvaluationErrorExitCode;
        }

        switch (result.Kind)
        {
            case ResultKind.Value when result.Value is { } value:
                console.WriteStandardOutputLine(roslyn.RenderRemoteValueToPlainText(value, Level.FirstSimple));
                return ExitCodes.Success;

            case ResultKind.Exception when result.Exception is { } exception:
                var (_, plainText) = roslyn.RenderRemoteException(exception, Level.FirstSimple);
                console.WriteStandardErrorLine(plainText);
                return EvaluationErrorExitCode;

            case ResultKind.Void:
            default:
                return ExitCodes.Success;
        }
    }
}
