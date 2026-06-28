// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using CSharpRepl.InjectedHook.Contracts;
using CSharpRepl.Services.Remote;
using Xunit;

namespace CSharpRepl.Tests;

/// <summary>
/// Test for cooperative eval cancellation. Cancelling a submission must keep the transport in sync (the
/// controller still receives exactly one result and the session survives) rather than tearing the process down
/// — the behavior the old Ctrl+C→Environment.Exit workaround couldn't provide. Cancellation is cooperative, so
/// a sleeping/CPU-bound submission isn't actually interrupted; the point under test is that the channel does
/// not desync and a subsequent evaluation still works.
/// </summary>
public class ConnectorCancellationTests
{
    [Fact(Timeout = 120_000)]
    public async Task Cancelling_AnInFlightEval_KeepsTheChannelInSyncAndTheSessionAlive()
    {
        using var process = ConnectorTestSupport.StartHookedTarget();
        var stdoutTask = process.StandardOutput.ReadToEndAsync(TestContext.Current.CancellationToken);
        var stderrTask = process.StandardError.ReadToEndAsync(TestContext.Current.CancellationToken);

        try
        {
            await using var session = await RemoteSession.ConnectAsync(process.Id, TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);

            // Cancel shortly after submitting a long-running evaluation. Cancellation is cooperative, so the
            // engine either reports cancellation or runs to completion — but either way exactly one result comes
            // back (the read is never abandoned) and the channel stays in lock-step.
            using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(250));
            var result = await session.EvalAsync("System.Threading.Thread.Sleep(1500); 123", detailed: false, cts.Token);

            Assert.True(result.Kind is ResultKind.Exception or ResultKind.Value, $"Unexpected result kind {result.Kind}.");
            if (result.Kind == ResultKind.Exception)
                Assert.Contains("cancel", result.Exception!.Message, StringComparison.OrdinalIgnoreCase);
            else
                Assert.Equal("123", result.Value!.DisplayText);

            // The channel is still in lock-step: a fresh evaluation succeeds on the surviving session.
            var afterCancel = await session.EvalAsync("7 * 6", detailed: false, TestContext.Current.CancellationToken);
            Assert.Equal("42", afterCancel.Value!.DisplayText);

            await session.DisconnectAsync(TestContext.Current.CancellationToken);
            await Task.Delay(300, TestContext.Current.CancellationToken);
            Assert.False(process.HasExited, "Target should keep running after a cancelled eval and disconnect.");
        }
        finally
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
            try { await Task.WhenAll(stdoutTask, stderrTask); } catch { /* output drained on process exit */ }
        }
    }
}
