// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Linq;
using System.Threading.Tasks;
using CSharpRepl.InjectedHook.Contracts;
using CSharpRepl.Repls;
using CSharpRepl.Services;
using CSharpRepl.Services.Remote;
using CSharpRepl.Services.Roslyn;
using NSubstitute;
using PrettyPrompt;
using Xunit;

namespace CSharpRepl.Tests;

/// <summary>
/// End-to-end test for the inspect-mode REPL loop. Launches the unmodified test target with the inspector
/// injected, builds the same controller stack Program.cs wires up (a real <see cref="RemoteSession"/> plus a
/// remote-configured <see cref="RoslynServices"/>), and drives <see cref="RemoteReadEvalPrintLoop.RunAsync"/>
/// with a scripted prompt: banner, commands, value/exception rendering through the themed renderer, the
/// committed-only workspace advance, and the graceful exit when the target process dies mid-session.
/// Per the no-TTY constraint the prompt itself is stubbed; everything below it is real.
/// </summary>
public class RemoteReadEvalPrintLoopTests
{
    private const string TargetType = "CSharpRepl.InjectedHook.TestTarget.Program";

    [Fact(Timeout = 180_000)]
    public async Task RunAsync_DrivesAFullInspectSession_AgainstAHookedProcess()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var process = InspectorTestSupport.StartHookedTarget();
        var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);

        try
        {
            await using var session = await RemoteSession.ConnectAsync(process.Id, TimeSpan.FromSeconds(30), cancellationToken);

            // The same controller stack Program.cs builds for inspect mode: editor services seeded with the
            // target's references and the inspector globals, rendering through the user's theme.
            var referencePaths = await session.GetReferencePathsAsync(cancellationToken);
            var (console, capturedStdout, _) = FakeConsole.CreateStubbedOutputAndError();
            var roslyn = new RoslynServices(console, new Configuration(), new TestTraceLogger(),
                new RemoteEditorContext(referencePaths, typeof(InspectorGlobals)));
            await roslyn.WarmUpAsync([]);

            var prompt = Substitute.For<IPrompt>();
            var repl = new RemoteReadEvalPrintLoop(console, session, roslyn, prompt);

            var detailedEnter = new ConsoleKeyInfo('\r', ConsoleKey.Enter, shift: false, alt: false, control: true);
            prompt.ReadLineAsync().Returns(
                new PromptResult(true, "help", default),
                new PromptResult(true, "clear", default),
                new PromptResult(true, $"var svc = {TargetType}.Shared;", default),       // void, committed
                new PromptResult(true, "svc.Value", default),                             // simple value render
                new PromptResult(true, "svc", detailedEnter),                             // detailed member render
                new PromptResult(true, "definitelyNotDefined", default),                  // compile error, loop survives
                new PromptResult(true, """throw new System.InvalidOperationException("bang!");""", default),
                new KeyPressCallbackResult("", "callback output marker"),                 // local key-binding output
                new PromptResult(true, "exit", default));

            await repl.RunAsync(new Configuration());

            var output = console.AnsiConsole.Output;

            // Banner built from the live handshake, plus the help command's inspect-mode text.
            Assert.Contains($"(pid {process.Id})", output);
            Assert.Contains("Inspect mode", output);
            console.Received().Clear();

            // The live target object rendered at both levels: its value, and its members in the detailed tree.
            Assert.Contains("41", output);    // svc.Value — Service.Value starts at 41
            Assert.Contains("Value", output); // the member name from the detailed render of `svc`

            // Both failure shapes rendered as errors without ending the loop (subsequent submissions ran).
            Assert.Contains("CS0103", output);
            Assert.Contains("bang!", output);

            // Key-binding callback output is surfaced via standard output, like the local loop.
            Assert.Contains("callback output marker", capturedStdout.ToString());

            // The committed `var` advanced the controller's remote workspace: the editor sees it afterwards.
            var completions = await roslyn.CompleteAsync("svc.", 4, cancellationToken);
            Assert.Contains("Next", completions.Select(c => c.Item.DisplayText));

            // --- A second run on the surviving session: losing the target ends the loop gracefully ---
            prompt.ReadLineAsync().Returns(
                _ => new PromptResult(true, "1 + 1", default),
                _ =>
                {
                    process.Kill(entireProcessTree: true);
                    process.WaitForExit();
                    return new PromptResult(true, "2 + 2", default); // evaluated against a dead target
                });

            await repl.RunAsync(new Configuration());

            Assert.Contains("2", console.AnsiConsole.Output); // the eval before the kill still worked
            Assert.Contains("Lost the connection to the target process", console.AnsiConsole.Output);
        }
        finally
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
            try { await Task.WhenAll(stdoutTask, stderrTask); } catch { /* output drained on process exit */ }
        }
    }
}
