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
/// End-to-end test for the connect-mode REPL loop. Launches the unmodified test target with the connector
/// injected, builds the same controller stack Program.cs wires up (a real <see cref="RemoteSession"/> plus a
/// remote-configured <see cref="RoslynServices"/>), and drives <see cref="RemoteReadEvalPrintLoop.RunAsync"/>
/// with a scripted prompt: banner, commands, value/exception rendering through the themed renderer, the
/// committed-only workspace advance, and the graceful exit when the target process dies mid-session.
/// Per the no-TTY constraint the prompt itself is stubbed; everything below it is real.
/// </summary>
public class RemoteReadEvalPrintLoopTests
{
    private const string TargetType = "CSharpRepl.InjectedHook.TestTarget.Program";
    private const string ServiceType = "CSharpRepl.InjectedHook.TestTarget.Service";

    [Fact(Timeout = 180_000)]
    public async Task RunAsync_DrivesAFullConnectSession_AgainstAHookedProcess()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var process = ConnectorTestSupport.StartHookedTarget();
        var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);

        try
        {
            await using var session = await RemoteSession.ConnectAsync(process.Id, TimeSpan.FromSeconds(30), cancellationToken);

            // The same controller stack Program.cs builds for connect mode: editor services seeded with the
            // target's references and the connector globals, rendering through the user's theme.
            var referencePaths = await session.GetReferencePathsAsync(cancellationToken);
            var (console, capturedStdout, _) = FakeConsole.CreateStubbedOutputAndError();
            var roslyn = new RoslynServices(console, new Configuration(), new TestTraceLogger(),
                new RemoteEditorContext(referencePaths, typeof(ConnectorGlobals)));
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
                // --- live method replacement: define a delegate, replace a live method, observe the change,
                //     list it, then revert it — the running target's behavior changes and reverts mid-session ---
                new PromptResult(true, $"System.Func<{ServiceType}, int, int> stub = (self, n) => 888;", default),
                new PromptResult(true, $"#replace {ServiceType}.Compute with stub", default),
                new PromptResult(true, "svc.Compute(111)", default),                      // detoured → 888
                new PromptResult(true, "#patches", default),
                new PromptResult(true, "#revert 1", default),
                new PromptResult(true, "svc.Compute(111)", default),                      // reverted → 222
                // --- command edge cases: usage error, engine failures, wrap, revert-all, empty listing ---
                new PromptResult(true, "#replace BadInput", default),                     // no " with " → usage error
                new PromptResult(true, $"#replace {ServiceType}.NoSuchMethod with stub", default), // engine Ok=false → "failed:"
                new PromptResult(true, "#revert 999", default),                           // no such patch → revert error
                new PromptResult(true, $"System.Func<System.Func<{ServiceType}, int, int>, {ServiceType}, int, int> wrapper = (orig, self, n) => orig(self, n) + 1000;", default),
                new PromptResult(true, $"#wrap {ServiceType}.Compute with wrapper", default),
                new PromptResult(true, "svc.Compute(111)", default),                      // wrapped → 222 + 1000 = 1222
                new PromptResult(true, "#patches", default),                              // lists the wrap (patch #2)
                new PromptResult(true, "#revert all", default),                           // → "reverted 1 patch(es)."
                new PromptResult(true, "#patches", default),                              // → "No active patches."
                new PromptResult(true, "exit", default));

            await repl.RunAsync(new Configuration());

            var output = console.AnsiConsole.Output;

            // Banner built from the live handshake, plus the help command's connect-mode text.
            Assert.Contains($"(pid {process.Id})", output);
            Assert.Contains("Connect mode", output);
            console.Received().Clear();

            // The live target object rendered at both levels: its value, and its members in the detailed tree.
            Assert.Contains("41", output);    // svc.Value — Service.Value starts at 41
            Assert.Contains("Value", output); // the member name from the detailed render of `svc`

            // Both failure shapes rendered as errors without ending the loop (subsequent submissions ran).
            Assert.Contains("CS0103", output);
            Assert.Contains("bang!", output);

            // Key-binding callback output is surfaced via standard output, like the local loop.
            Assert.Contains("callback output marker", capturedStdout.ToString());

            // Live method replacement: the detour took effect (888), was listed, then reverted (222 = 111 * 2).
            Assert.Contains("patched", output);
            Assert.Contains("888", output);
            Assert.Contains("#1", output);              // #patches listing / revert confirmation
            Assert.Contains("222", output);             // original behavior after #revert

            // Command edge cases, each a distinct PrintCommandResult arm rendered through the real loop:
            Assert.Contains("Usage:", output);                  // "#replace BadInput" → usage error
            Assert.Contains("failed", output);                  // "#replace …NoSuchMethod" → engine Ok=false
            Assert.Contains("No active patch with id", output); // "#revert 999" → revert error
            Assert.Contains("wrapped", output);                 // "#wrap" success
            Assert.Contains("1222", output);                    // wrapped result (orig 222 + 1000)
            Assert.Contains("patch(es)", output);               // "#revert all" → "reverted N patch(es)."
            Assert.Contains("No active patches.", output);      // empty "#patches" after revert-all

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
