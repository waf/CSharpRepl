// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CSharpRepl.InjectedHook.Contracts;
using CSharpRepl.Services.Remote;
using Xunit;

namespace CSharpRepl.Tests;

/// <summary>
/// End-to-end test for the cooperative in-process Roslyn connector. Launches the unmodified test target
/// as a separate process with the connector injected via DOTNET_STARTUP_HOOKS, connects over the transport,
/// and exercises the full round-trip: handshake, value evaluation, live static read, REPL parity (a var and
/// a declared method reused across submissions binding to the live object), exception survival, cross-process
/// write-back, and a graceful disconnect leaving the target running.
/// </summary>
public class ConnectorRoundTripTests
{
    private const string TargetType = "CSharpRepl.InjectedHook.TestTarget.Program";

    [Fact(Timeout = 120_000)]
    public async Task RoundTrip_AgainstHookedProcess_EvaluatesReadsWritesAndDisconnects()
    {
        using var process = ConnectorTestSupport.StartHookedTarget();

        // Drain the child's output so a full pipe buffer can't block it; results are only needed for diagnostics.
        var stdoutTask = process.StandardOutput.ReadToEndAsync(TestContext.Current.CancellationToken);
        var stderrTask = process.StandardError.ReadToEndAsync(TestContext.Current.CancellationToken);

        try
        {
            await using var session = await ConnectAsync(process);

            // --- Handshake ---
            Assert.Equal(process.Id, session.Handshake.ProcessId);
            Assert.Equal(ConnectorTransport.ProtocolVersion, session.Handshake.ProtocolVersion);
            Assert.False(string.IsNullOrEmpty(session.Handshake.SessionId));
            // A normal `dotnet App` launch has its assemblies on disk (not a single-file bundle).
            Assert.Equal(TargetAssemblyAvailability.Normal, session.Handshake.AssemblyAvailability);

            // --- Basic value round-trip (the friendly C# type name is projected) ---
            var onePlusOne = await EvalAsync(session, "1 + 1");
            Assert.Equal(ResultKind.Value, onePlusOne.Kind);
            Assert.Equal(RemoteValueKind.Scalar, onePlusOne.Value!.Kind);
            Assert.Equal("2", onePlusOne.Value.DisplayText);
            Assert.Equal("int", onePlusOne.Value.TypeName);

            // --- Live static read (binds to the target's real, climbing counter) ---
            var counter = await EvalAsync(session, $"{TargetType}.Counter");
            Assert.Equal(ResultKind.Value, counter.Kind);
            Assert.True(int.Parse(counter.Value!.DisplayText) > 0, $"Counter should be climbing, was {counter.Value.DisplayText}");

            // --- REPL parity: a var and a declared method persist across submissions ---
            var bindShared = await EvalAsync(session, $"var s = {TargetType}.Shared;");
            Assert.Equal(ResultKind.Void, bindShared.Kind);

            var next = await EvalAsync(session, "s.Next()"); // Service.Value starts at 41 → 42
            Assert.Equal(ResultKind.Value, next.Kind);
            Assert.Equal("42", next.Value!.DisplayText);

            // The submission mutated the SAME live instance the target holds (not a copy).
            var sharedValue = await EvalAsync(session, $"{TargetType}.Shared.Value");
            Assert.Equal("42", sharedValue.Value!.DisplayText);

            var declareMethod = await EvalAsync(session, "int Twice(int n) => n * 2;");
            Assert.Equal(ResultKind.Void, declareMethod.Kind);

            var useMethodAndVar = await EvalAsync(session, "Twice(s.Value)"); // Twice(42) == 84
            Assert.Equal("84", useMethodAndVar.Value!.DisplayText);

            // --- Exception path: surfaced as an exception result; the session survives ---
            var thrown = await EvalAsync(session, "throw new System.InvalidOperationException(\"boom\");");
            Assert.Equal(ResultKind.Exception, thrown.Kind);
            Assert.Contains("boom", thrown.Exception!.Message);

            var afterThrow = await EvalAsync(session, "7 * 6");
            Assert.Equal("42", afterThrow.Value!.DisplayText);

            // --- Cross-process write-back to a real static ---
            var write = await EvalAsync(session, $"{TargetType}.WriteProbe = 9999;");
            Assert.Equal(ResultKind.Void, write.Kind);
            var readBack = await EvalAsync(session, $"{TargetType}.WriteProbe");
            Assert.Equal("9999", readBack.Value!.DisplayText);

            // --- A huge scalar string is capped in the projection (it never ships whole over the wire) ---
            var longString = await EvalAsync(session, "new string('a', 50000)");
            Assert.Equal(RemoteValueKind.Scalar, longString.Value!.Kind);
            Assert.True(longString.Value.DisplayText.Length < 20_000, $"Long string should be capped, was {longString.Value.DisplayText.Length} chars.");
            Assert.Contains("more chars", longString.Value.DisplayText);

            // --- Value-vs-void detection — a null-returning expression is a value (null), not void ---
            var nullExpression = await EvalAsync(session, "(string)null");
            Assert.Equal(ResultKind.Value, nullExpression.Kind);
            Assert.True(nullExpression.Value!.IsNull, "A null-returning expression should render as a null value, not void.");
            Assert.Equal(RemoteValueKind.Null, nullExpression.Value.Kind);

            // A string is a scalar, quoted like the local REPL.
            var stringValue = await EvalAsync(session, "\"hello\"");
            Assert.Equal(RemoteValueKind.Scalar, stringValue.Value!.Kind);
            Assert.Equal("\"hello\"", stringValue.Value.DisplayText);

            // --- Collection projection (elements + count) ---
            var collection = await EvalAsync(session, "new[] { 10, 20, 30 }");
            Assert.Equal(RemoteValueKind.Collection, collection.Value!.Kind);
            Assert.Equal(3, collection.Value.Count);
            Assert.NotNull(collection.Value.Items);
            Assert.Equal(["10", "20", "30"], collection.Value.Items!.Select(i => i.DisplayText));

            // --- A cyclic collection must not recurse unboundedly (a StackOverflowException would kill the
            //         target). Nested collections are depth-limited to a type-name + count header. ---
            var cyclic = await EvalAsync(session, "var cycle = new List<object>(); cycle.Add(cycle); cycle");
            Assert.Equal(ResultKind.Value, cyclic.Kind);
            Assert.Equal(RemoteValueKind.Collection, cyclic.Value!.Kind);
            var nested = Assert.Single(cyclic.Value.Items!);
            Assert.Equal(RemoteValueKind.Collection, nested.Kind);
            Assert.Equal(1, nested.Count);
            Assert.Null(nested.Items); // header only — the recursion stops here

            // --- DI-root capture: the bootstrap captures the host's root provider from the HostBuilt
            //         DiagnosticListener event, and Get<T>() resolves the target's real live singleton.
            //         The capture races with connecting (the transport is up before Main builds the host),
            //         so poll: the globals read ConnectorRoots live, no reconnect needed. ---
            await WaitForDiCaptureAsync(session);
            var resolved = await EvalAsync(session, "Get<CSharpRepl.InjectedHook.TestTarget.Service>().Value");
            Assert.Equal(ResultKind.Value, resolved.Kind);
            Assert.Equal("42", resolved.Value!.DisplayText); // the registered singleton IS Program.Shared (mutated to 42 above)

            // --- Object projection — members are projected only for the detailed view (so getters aren't
            //         invoked for the simple summary), matching the local REPL. ---
            var objSimple = await EvalAsync(session, $"{TargetType}.Shared");
            Assert.Equal(RemoteValueKind.Object, objSimple.Value!.Kind);
            Assert.Null(objSimple.Value.Members); // simple view: summary only, no member reflection

            var objDetailed = await EvalAsync(session, $"{TargetType}.Shared", detailed: true);
            Assert.Equal(RemoteValueKind.Object, objDetailed.Value!.Kind);
            Assert.NotNull(objDetailed.Value.Members);
            var valueMember = Assert.Single(objDetailed.Value.Members!, m => m.Name == "Value");
            Assert.Equal("42", valueMember.Value.DisplayText); // mutated to 42 by the parity submission above

            // --- Graceful disconnect; the target keeps running ---
            await session.DisconnectAsync(TestContext.Current.CancellationToken);
            await Task.Delay(500, TestContext.Current.CancellationToken);
            Assert.False(process.HasExited, "Target should keep running after the controller disconnects.");

            // --- Reconnect: the server accepts a new controller, and this handshake (built after the host
            //         was captured above) reports the DI provider. ---
            await using var secondSession = await ConnectAsync(process);
            Assert.True(secondSession.Handshake.DiProviderCaptured,
                "A handshake built after the target's host was captured should report DiProviderCaptured.");
        }
        finally
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
            try { await Task.WhenAll(stdoutTask, stderrTask); } catch { /* output drained on process exit */ }
        }
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

    private static Task<EvalResponse> EvalAsync(RemoteSession session, string code, bool detailed = false)
        => session.EvalAsync(code, detailed, TestContext.Current.CancellationToken);

    /// <summary>
    /// Waits for the bootstrap's HostCapture to observe the target's HostBuilt event. The capture happens
    /// when the target's Main builds its host, which races with the controller connecting (the transport
    /// listener is up before Main); the globals read the captured provider live, so polling an eval suffices.
    /// </summary>
    private static async Task WaitForDiCaptureAsync(RemoteSession session)
    {
        var deadline = Stopwatch.StartNew();
        while (true)
        {
            var captured = await EvalAsync(session, "services is not null");
            if (captured.Value?.DisplayText == "true")
                return;

            Assert.True(deadline.Elapsed < TimeSpan.FromSeconds(15),
                "The target builds a Generic Host, so the DI provider should have been captured.");
            await Task.Delay(200, TestContext.Current.CancellationToken);
        }
    }
}
