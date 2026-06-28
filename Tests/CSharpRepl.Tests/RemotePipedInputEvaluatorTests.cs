// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Threading.Tasks;
using CSharpRepl.Repls;
using CSharpRepl.Services;
using CSharpRepl.Services.Remote;
using CSharpRepl.Services.Roslyn;
using NSubstitute;
using Xunit;

namespace CSharpRepl.Tests;

/// <summary>
/// End-to-end test for the non-interactive connect path: drives <see cref="RemotePipedInputEvaluator"/> against a
/// real hooked child via a real <see cref="RemoteSession"/>. Verifies clean plain-text stdout, errors to stderr
/// with a nonzero exit, connect commands, and that engine state persists across separate evaluations.
/// </summary>
public class RemotePipedInputEvaluatorTests
{
    private const string TargetType = "CSharpRepl.InjectedHook.TestTarget.Program";

    [Fact(Timeout = 180_000)]
    public async Task EvaluatesNonInteractively_AgainstAHookedProcess()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var process = ConnectorTestSupport.StartHookedTarget();
        var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);

        try
        {
            await using var session = await RemoteSession.ConnectAsync(process.Id, TimeSpan.FromSeconds(30), cancellationToken);

            // --- A value-returning expression auto-prints as plain text on stdout, exit 0 ---
            {
                var (console, stdout, stderr) = FakeConsole.CreateStubbedOutputAndError();
                console.PrettyPromptConsole.IsErrorRedirected = true;
                var evaluator = NewEvaluator(console, session);

                var exitCode = await evaluator.EvaluateStringAsync("41 + 1");

                Assert.Equal(ExitCodes.Success, exitCode);
                Assert.Equal("42", stdout.ToString().Trim());
                Assert.Equal("", stderr.ToString().Trim());
                // Output must be free of ANSI escape sequences so it's safe to capture/pipe.
                Assert.DoesNotContain('\x1b', stdout.ToString());
            }

            // --- State persists across separate connections: declare in one call, read it back in the next ---
            {
                var (console1, _, _) = FakeConsole.CreateStubbedOutputAndError();
                Assert.Equal(ExitCodes.Success,
                    await NewEvaluator(console1, session).EvaluateStringAsync($"var probe = {TargetType}.Shared.Value;"));

                var (console2, stdout2, _) = FakeConsole.CreateStubbedOutputAndError();
                Assert.Equal(ExitCodes.Success, await NewEvaluator(console2, session).EvaluateStringAsync("probe"));
                Assert.Equal("41", stdout2.ToString().Trim()); // Service.Value starts at 41
            }

            // --- A runtime exception goes to stderr with a nonzero exit and nothing on stdout ---
            {
                var (console, stdout, stderr) = FakeConsole.CreateStubbedOutputAndError();
                console.PrettyPromptConsole.IsErrorRedirected = true;
                var evaluator = NewEvaluator(console, session);

                var exitCode = await evaluator.EvaluateStringAsync(@"throw new System.InvalidOperationException(""boom"");");

                Assert.NotEqual(ExitCodes.Success, exitCode);
                Assert.Equal("", stdout.ToString().Trim());
                Assert.Contains("boom", stderr.ToString());
            }

            // --- A connect command (#patches) is honored non-interactively and renders to stdout ---
            {
                var (console, stdout, _) = FakeConsole.CreateStubbedOutputAndError();
                var evaluator = NewEvaluator(console, session);

                var exitCode = await evaluator.EvaluateStringAsync("#patches");

                Assert.Equal(ExitCodes.Success, exitCode);
                Assert.Contains("No active patches.", stdout.ToString());
            }

            // --- Piped stdin: lines are collected and evaluated as a single submission ---
            {
                var (console, stdout, _) = FakeConsole.CreateStubbedOutputAndError();
                console.ReadLine().Returns("var x = 20;", "var y = 22;", "x + y", (string)null);
                var evaluator = NewEvaluator(console, session);

                var exitCode = await evaluator.EvaluateCollectedPipeInputAsync();

                Assert.Equal(ExitCodes.Success, exitCode);
                Assert.Equal("42", stdout.ToString().Trim());
            }

            // --- Streaming piped input: a command is handled before the completeness gate, then each complete
            //     statement is evaluated in turn (covers the remote handleBeforeGate path of PipedInputReader) ---
            {
                var (console, stdout, _) = FakeConsole.CreateStubbedOutputAndError();
                console.ReadLine().Returns("#patches", "var z = 100;", "z + 1", (string)null);
                var evaluator = NewEvaluator(console, session);

                var exitCode = await evaluator.EvaluateStreamingPipeInputAsync();

                Assert.Equal(ExitCodes.Success, exitCode);
                Assert.Contains("No active patches.", stdout.ToString()); // the #patches command
                Assert.Contains("101", stdout.ToString());                // z + 1
            }
        }
        finally
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
            try { await Task.WhenAll(stdoutTask, stderrTask); } catch { /* output drained on process exit */ }
        }
    }

    // Mirrors the non-interactive stack Program.cs builds: a reference-less RoslynServices, no prompt.
    private static RemotePipedInputEvaluator NewEvaluator(FakeConsoleAbstract console, RemoteSession session) =>
        new(console, session, new RoslynServices(console, new Configuration(), new TestTraceLogger()));
}
