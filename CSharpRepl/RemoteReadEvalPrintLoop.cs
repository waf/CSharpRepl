// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CSharpRepl.InjectedHook.Contracts;
using CSharpRepl.PrettyPromptConfig;
using CSharpRepl.Services;
using CSharpRepl.Services.Remote;
using CSharpRepl.Services.Roslyn;
using CSharpRepl.Services.Roslyn.Formatting;
using PrettyPrompt;
using PrettyPrompt.Consoles;
using Spectre.Console;

namespace CSharpRepl;

/// <summary>
/// The inspect-mode REPL: collects input with the same <see cref="PrettyPrompt"/> prompt as the local loop,
/// but routes each submission to the inspector hosted in the target process (<see cref="RemoteSession"/>)
/// instead of the local <see cref="RoslynServices"/>, and renders the returned <see cref="RemoteValue"/> /
/// <see cref="RemoteException"/> through the controller's themed <see cref="RemoteValueRenderer"/>.
/// </summary>
internal sealed class RemoteReadEvalPrintLoop
{
    private readonly IConsoleService console;
    private readonly RemoteSession session;
    private readonly RoslynServices roslyn;
    private readonly IPrompt prompt;

    public RemoteReadEvalPrintLoop(IConsoleService console, RemoteSession session, RoslynServices roslyn, IPrompt prompt)
    {
        this.console = console;
        this.session = session;
        this.roslyn = roslyn;
        this.prompt = prompt;
    }

    public async Task RunAsync(Configuration config)
    {
        PrintBanner(session.Handshake);

        while (true)
        {
            var response = await prompt.ReadLineAsync().ConfigureAwait(false);

            if (response is ExitApplicationKeyPress)
            {
                break;
            }

            if (!response.IsSuccess)
            {
                continue;
            }

            var commandText = response.Text.Trim();
            var command = commandText.ToLowerInvariant();

            if (command == "exit") { break; }
            if (command == "clear") { console.Clear(); continue; }
            if (command is "help" or "#help" or "?") { PrintHelp(); continue; }

            // Local key-binding callbacks (e.g. lowering/IL) still operate on the local Roslyn services; they're
            // advisory in remote mode but harmless, so surface their output the same way the local loop does.
            if (response is KeyPressCallbackResult callbackOutput)
            {
                console.WriteStandardOutputLine(Environment.NewLine + callbackOutput.Output);
                continue;
            }

            // Ctrl+C cancels the in-flight evaluation cooperatively: EvalAsync sends a cancel to the engine and
            // still awaits the (cancelled or completed) result, so the channel stays in sync and the session
            // survives — no longer tearing down the controller process. Cancellation is cooperative, so it can't
            // interrupt arbitrary running user code in the target (same limitation as the local REPL).
            var detailed = config.SubmitPromptDetailedKeys.Matches(response.SubmitKeyInfo);
            var level = detailed ? Level.FirstDetailed : Level.FirstSimple;

            EvalResponse result;
            try
            {
                result = await session.EvalAsync(response.Text, detailed, response.CancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                continue;
            }
            catch (IOException ex)
            {
                console.WriteErrorLine($"Lost the connection to the target process: {ex.Message}");
                break;
            }

            // Keep the controller's editor workspace in sync with the target's submission chain: only a
            // committed submission (one that extended the engine's ScriptState) becomes a new document, so
            // completion/highlighting see its locals, declared methods, and types — mirroring how the local
            // RoslynServices.EvaluateAsync advances its workspace only on a successful evaluation.
            if (result.Committed)
            {
                // The target already committed, so always advance (don't honor the submission's cancellation
                // token here) to keep the controller's workspace from drifting behind the engine's chain.
                await roslyn.AdvanceRemoteWorkspaceAsync(response.Text, System.Threading.CancellationToken.None).ConfigureAwait(false);
            }

            Print(result, level);
        }
    }

    private void Print(EvalResponse result, Level level)
    {
        switch (result.Kind)
        {
            case ResultKind.Value when result.Value is { } value:
                console.Write(roslyn.RenderRemoteValue(value, level));
                console.WriteLine();
                break;

            case ResultKind.Exception when result.Exception is { } exception:
                var (renderable, plainText) = roslyn.RenderRemoteException(exception, level);
                console.WriteError(renderable, plainText);
                console.WriteLine();
                break;

            case ResultKind.Void:
            default:
                console.WriteLine();
                break;
        }
    }

    private void PrintBanner(HandshakeMessage handshake)
    {
        console.WriteLine($"Connected to {handshake.ProcessName} (pid {handshake.ProcessId})");
        console.WriteLine($"  Runtime:   {handshake.RuntimeVersion}");
        console.WriteLine($"  Inspector: v{handshake.InspectorVersion} (protocol v{handshake.ProtocolVersion})");
        console.WriteLine(handshake.DiProviderCaptured
            ? "  DI provider captured: yes — `services` and `Get<T>()` are available."
            : "  DI provider captured: no — only statics and framework code are reachable.");

        if (handshake.AssemblyAvailability == TargetAssemblyAvailability.FrameworkDependentSingleFile)
        {
            console.Write(new Markup(
                "[yellow]  Reflection mode: this is a framework-dependent single-file app, so its own assemblies are " +
                "bundled and have no metadata. Framework code works, but typed access to the target's own types fails " +
                "with CS0103 — reach the target's state via reflection, e.g. Type.GetType(\"MyApp.Program, MyApp\").[/]"));
            console.WriteLine();
        }

        if (handshake.ProtocolVersion != InspectorTransport.ProtocolVersion)
        {
            console.Write(new Markup(
                $"[yellow]Warning: the target's inspector protocol (v{handshake.ProtocolVersion}) differs from this tool's (v{InspectorTransport.ProtocolVersion}); behavior may be inconsistent.[/]"));
            console.WriteLine();
        }

        console.WriteLine(string.Empty);
        console.Write(new Markup(
            "[yellow]⚠ Development/diagnostics tool: code you evaluate runs inside the target process with its full privileges. Never inspect a production process.[/]"));
        console.WriteLine();
        console.WriteLine("Type C# to evaluate it in the target. Type exit (or press Ctrl+D) to detach; the target keeps running and you can reconnect later.");
        console.WriteLine(string.Empty);
    }

    private void PrintHelp()
    {
        console.Write(new Markup(
            """
            [underline]Inspect mode[/]
            Submissions are evaluated inside the target process and share a persistent state chain, so a
            [green]var[/] or a declared method on one line is reusable on the next — exactly like the local REPL.

            Reachable state:
              - Statics: reference them by their fully-qualified name, e.g. [green]MyApp.Program.SomeStatic[/].
              - DI services (when captured): [green]services.GetRequiredService<T>()[/] or [green]Get<T>()[/].

            Commands:
              - [green]exit[/]: detach and quit the REPL; the target keeps running and you can reconnect later.
              - [green]clear[/]: clear the terminal.
            """));
        console.WriteLine();
    }
}
