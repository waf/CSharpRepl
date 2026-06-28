// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using CSharpRepl.InjectedHook.Contracts;
using CSharpRepl.PrettyPromptConfig;
using CSharpRepl.Services;
using CSharpRepl.Services.Remote;
using CSharpRepl.Services.Roslyn;
using Microsoft.CodeAnalysis.Classification;
using Microsoft.CodeAnalysis.Text;
using PrettyPrompt;
using PrettyPrompt.Consoles;
using Xunit;

namespace CSharpRepl.Tests;

/// <summary>
/// End-to-end test for the controller-side remote editor services. Launches the unmodified test target with
/// the connector injected, fetches the target's loaded-assembly paths over the wire, and builds a
/// remote-configured <see cref="RoslynServices"/> from them. Then asserts the editor services are target-aware:
/// the connector globals (<c>services</c>/<c>Get</c>) complete, the target's own types complete and highlight
/// as types, and a declared <c>var</c> from a committed submission is usable on the next line — the
/// cross-process analogue of the local REPL's submission-chain parity.
/// </summary>
/// <remarks>
/// Per the no-TTY constraint, this drives the editor services directly (CompleteAsync/SyntaxHighlightAsync),
/// not through the live PrettyPrompt loop. WarmUpAsync is used only to deterministically await initialization.
/// </remarks>
public class RemoteEditorServicesTests
{
    private const string TargetNamespace = "CSharpRepl.InjectedHook.TestTarget";

    [Fact(Timeout = 120_000)]
    public async Task RemoteEditor_AgainstHookedProcess_IsTargetAware()
    {
        using var process = ConnectorTestSupport.StartHookedTarget();
        var stdoutTask = process.StandardOutput.ReadToEndAsync(TestContext.Current.CancellationToken);
        var stderrTask = process.StandardError.ReadToEndAsync(TestContext.Current.CancellationToken);

        try
        {
            await using var session = await ConnectAsync(process);

            // --- The wire path that seeds the editor: the engine reports the target's loaded assemblies ---
            var referencePaths = await session.GetReferencePathsAsync(TestContext.Current.CancellationToken);
            Assert.Contains(referencePaths, p => p.EndsWith($"{TargetNamespace}.dll", StringComparison.OrdinalIgnoreCase));

            var (console, _) = FakeConsole.CreateStubbedOutput();
            var remoteEditor = new RemoteEditorContext(referencePaths, typeof(ConnectorGlobals));
            var services = new RoslynServices(console, new Configuration(theme: "Data/theme.json"), new TestTraceLogger(), remoteEditor);
            await services.WarmUpAsync([]); // awaits background initialization and warms the editor path

            // --- Connector globals are in scope via the workspace's host object type ---
            Assert.Contains("services", await CompletionDisplayTextsAsync(services, "ser"));
            Assert.Contains("Get", await CompletionDisplayTextsAsync(services, "Ge"));

            // --- The target's own types complete (proves the target references seeded the workspace) ---
            var namespaceMembers = await CompletionDisplayTextsAsync(services, TargetNamespace + ".");
            Assert.Contains("Service", namespaceMembers);
            Assert.Contains("Program", namespaceMembers);

            // --- Submission-chain parity: a committed `var` is usable (with its members) on the next line ---
            await services.AdvanceRemoteWorkspaceAsync($"var s = {TargetNamespace}.Program.Shared;", TestContext.Current.CancellationToken);
            var memberCompletions = await CompletionDisplayTextsAsync(services, "s.");
            Assert.Contains("Value", memberCompletions); // Service.Value
            Assert.Contains("Next", memberCompletions);  // Service.Next()

            // --- #replace completion: the target's types complete in the type-path segment ---
            var replaceTypes = await CompletionDisplayTextsAsync(services, $"#replace {TargetNamespace}.");
            Assert.Contains("Service", replaceTypes);
            Assert.Contains("Program", replaceTypes);

            // --- #replace completion: a type's INSTANCE members complete (nameof rewrite, not static-only) ---
            var replaceMembers = await CompletionDisplayTextsAsync(services, $"#replace {TargetNamespace}.Service.");
            Assert.Contains("Compute", replaceMembers); // instance method
            Assert.Contains("Next", replaceMembers);    // instance method

            // --- The span committed for a #replace target is the identifier under the caret, not the whole line ---
            var partial = $"#replace {TargetNamespace}.Servi";
            var span = await services.GetSpanToReplaceByCompletionAsync(partial, partial.Length, TestContext.Current.CancellationToken);
            Assert.Equal("Servi", partial.Substring(span.Start, span.Length));

            // --- The replacement-expression position completes ordinary script code against the chain (s.) ---
            var replaceExpr = await CompletionDisplayTextsAsync(services, $"#replace {TargetNamespace}.Service.Compute with s.");
            Assert.Contains("Value", replaceExpr);
            Assert.Contains("Compute", replaceExpr);

            // --- Typing into a #replace argument opens the completion window (ReplaceMethodCompletionProvider
            //     .ShouldTriggerCompletion): built-in providers don't engage on the bad-directive line, so ours does ---
            var dotKey = new KeyPress(new ConsoleKeyInfo('.', ConsoleKey.OemPeriod, shift: false, alt: false, control: false));
            var afterDot = $"#replace {TargetNamespace}.Service.";
            Assert.True(await services.ShouldOpenCompletionWindowAsync(afterDot, afterDot.Length, dotKey, TestContext.Current.CancellationToken));

            var letterKey = new KeyPress(new ConsoleKeyInfo('i', ConsoleKey.I, shift: false, alt: false, control: false));
            var afterLetter = $"#replace {TargetNamespace}.Servi";
            Assert.True(await services.ShouldOpenCompletionWindowAsync(afterLetter, afterLetter.Length, letterKey, TestContext.Current.CancellationToken));

            // A non-command line: the connect provider declines (TryRewrite false), leaving the decision to others.
            var ordinaryLine = $"{TargetNamespace}.Servi";
            await services.ShouldOpenCompletionWindowAsync(ordinaryLine, ordinaryLine.Length, letterKey, TestContext.Current.CancellationToken);

            // --- A #replace member completion produces a tooltip (ReplaceMethodCompletionProvider.GetDescriptionAsync,
            //     which rebuilds the synthetic document and forwards to the real member's description) ---
            var describeLine = $"#replace {TargetNamespace}.Service.";
            var memberItems = await services.CompleteAsync(describeLine, describeLine.Length, TestContext.Current.CancellationToken);
            var computeItem = memberItems.First(c => c.Item.DisplayText == "Compute");
            var description = await computeItem.GetDescriptionAsync(TestContext.Current.CancellationToken);
            Assert.Contains("Compute", description.Text);

            // --- The connect commands themselves complete (with help text), via the prompt callbacks ---
            var promptCallbacks = new CSharpReplPromptCallbacks(console, services, new Configuration(theme: "Data/theme.json"));
            var commandItems = await promptCallbacks.GetCompletionItemsCoreAsync("#re", 3, TestContext.Current.CancellationToken);
            var commandTexts = commandItems.Select(c => c.ReplacementText).ToList();
            Assert.Contains("#replace", commandTexts);
            Assert.Contains("#wrap", commandTexts);
            Assert.Contains("#patches", commandTexts);
            Assert.Contains("#revert", commandTexts);
            var replaceDescription = await commandItems.First(c => c.ReplacementText == "#replace")
                .GetExtendedDescriptionAsync(TestContext.Current.CancellationToken);
            Assert.Contains("Replace a live method", replaceDescription.Text);

            // --- Semantic highlighting resolves the target type as a class (not an unresolved identifier) ---
            var classNameColor = services.ToColor(ClassificationTypeNames.ClassName);
            var typeReference = $"{TargetNamespace}.Service";
            var serviceSpan = new TextSpan(TargetNamespace.Length + 1, "Service".Length);
            var highlights = await services.SyntaxHighlightAsync(typeReference);
            Assert.Contains(highlights, h => h.TextSpan == serviceSpan && h.Color == classNameColor);
        }
        finally
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
            try { await Task.WhenAll(stdoutTask, stderrTask); } catch { /* output drained on process exit */ }
        }
    }

    private static async Task<IReadOnlyList<string>> CompletionDisplayTextsAsync(RoslynServices services, string text)
    {
        var completions = await services.CompleteAsync(text, text.Length, TestContext.Current.CancellationToken);
        return completions.Select(c => c.Item.DisplayText).ToList();
    }

    private static async Task<RemoteSession> ConnectAsync(Process process)
    {
        try
        {
            return await RemoteSession.ConnectAsync(process.Id, TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);
        }
        catch (Exception ex) when (process.HasExited)
        {
            throw new InvalidOperationException(
                $"The target process exited (code {process.ExitCode}) before the connector connection succeeded.", ex);
        }
    }
}
