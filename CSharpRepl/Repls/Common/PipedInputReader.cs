// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Text;
using System.Threading.Tasks;
using CSharpRepl.Services;

namespace CSharpRepl.Repls.Common;

/// <summary>
/// Shared stdin-reading mechanics for the non-interactive evaluators (<see cref="PipedInputEvaluator"/> and
/// <see cref="RemotePipedInputEvaluator"/>): collecting all input as one submission, or streaming it line by
/// line batched into complete statements. The per-submission action (local vs remote evaluation) is supplied
/// by the caller as a delegate.
/// </summary>
internal static class PipedInputReader
{
    /// <summary>Reads all of stdin into a single string. Could block forever if input never ends.</summary>
    public static string ReadAll(IConsoleService console)
    {
        var input = new StringBuilder();
        while (console.ReadLine() is string line)
        {
            input.AppendLine(line);
        }
        return input.ToString();
    }

    /// <summary>
    /// Reads stdin line by line, batching into complete statements (so a partial statement isn't evaluated
    /// early) and invoking <paramref name="evaluate"/> on each. Returns the first non-success exit code, else
    /// <see cref="ExitCodes.Success"/>. <paramref name="intercept"/> is called on each line for custom logic,
    /// used by the remote evaluator for non-c# commands like "#replace"
    /// </summary>
    public static async Task<int> StreamAsync(
        IConsoleService console,
        Func<string, Task<bool>> isComplete,
        Func<string, Task<int>> evaluate,
        Func<string, Task<(bool Handled, int ExitCode)>>? intercept = null)
    {
        var statement = new StringBuilder();
        while (console.ReadLine() is string line)
        {
            statement.AppendLine(line);
            var text = statement.ToString();

            if (intercept is not null)
            {
                var (handled, exitCode) = await intercept(text.Trim()).ConfigureAwait(false);
                if (handled)
                {
                    statement.Clear();
                    if (exitCode != ExitCodes.Success) return exitCode;
                    continue;
                }
            }

            if (!await isComplete(text).ConfigureAwait(false))
            {
                continue;
            }
            statement.Clear();

            var result = await evaluate(text).ConfigureAwait(false);
            if (result != ExitCodes.Success) return result;
        }

        return ExitCodes.Success;
    }
}
