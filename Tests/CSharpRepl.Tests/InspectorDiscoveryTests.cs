// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

#nullable enable

using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CSharpRepl.InjectedHook.Contracts;
using Xunit;

namespace CSharpRepl.Tests;

/// <summary>
/// Tests for the `inspect list` discovery layer (<see cref="InspectorTransport.TryParseProcessId"/> and
/// <see cref="InspectorTransport.EnumerateListeningProcessIds"/>).
/// A mix of unit tests (parsing) and integration tests (launch a real hooked target and proves the 
/// target's pid is discovered while a non-hooked process (this test runner) is not.
/// </summary>
public class InspectorDiscoveryTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(1234)]
    [InlineData(int.MaxValue)]
    public void TryParseProcessId_RoundTripsTheEndpointNamesItBuilds(int processId)
    {
        // Round-trip through the real builders so the parser and PipeName/SocketPath can't drift apart.
        Assert.True(InspectorTransport.TryParseProcessId(InspectorTransport.PipeName(processId), out var fromPipe));
        Assert.Equal(processId, fromPipe);

        var socketName = Path.GetFileName(InspectorTransport.SocketPath(processId));
        Assert.True(InspectorTransport.TryParseProcessId(socketName, out var fromSocket));
        Assert.Equal(processId, fromSocket);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("CSharpRepl.InjectedHook.")]      // our prefix but no number
    [InlineData("CSharpRepl.InjectedHook.abc")]   // not numeric
    [InlineData("CSharpRepl.InjectedHook.-5")]    // a sign is not a bare digit run
    [InlineData("CSharpRepl.InjectedHook.0")]     // pids are positive
    [InlineData("CSharpRepl.InjectedHook.12.3")]  // not an integer
    [InlineData("dotnet-diagnostic-1234")]        // a real .NET diagnostic-port pipe must NOT be claimed as ours
    [InlineData("inspector-1234.txt")]            // wrong suffix
    [InlineData("inspector-.sock")]               // socket form but no number
    [InlineData("inspector-abc.sock")]            // socket form, not numeric
    [InlineData("some-unrelated-pipe")]
    public void TryParseProcessId_RejectsAnythingThatIsNotOurEndpoint(string? endpointName)
    {
        Assert.False(InspectorTransport.TryParseProcessId(endpointName!, out _));
    }

    [Fact]
    public void EnumerateListeningProcessIds_DoesNotListThisNonHookedProcess()
    {
        // The test runner wasn't launched with the inspector hook, so it must never appear as attachable.
        Assert.DoesNotContain(Environment.ProcessId, InspectorTransport.EnumerateListeningProcessIds());
    }

    [Fact(Timeout = 120_000)]
    public async Task EnumerateListeningProcessIds_FindsAHookedTarget()
    {
        using var process = InspectorTestSupport.StartHookedTarget();

        // Drain the child's output so a full pipe buffer can't block it; the content isn't needed.
        var stdoutTask = process.StandardOutput.ReadToEndAsync(TestContext.Current.CancellationToken);
        var stderrTask = process.StandardError.ReadToEndAsync(TestContext.Current.CancellationToken);

        try
        {
            // The listener comes up on a background thread shortly after the process starts (before Main),
            // so poll rather than assuming it's immediately present.
            var found = await WaitUntilListedAsync(process.Id, TimeSpan.FromSeconds(30));
            Assert.True(found,
                $"The hooked target (pid {process.Id}) should be discoverable via its inspector endpoint.");
        }
        finally
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
            try { await Task.WhenAll(stdoutTask, stderrTask); } catch { /* output drained on process exit */ }
        }
    }

    private static async Task<bool> WaitUntilListedAsync(int processId, TimeSpan timeout)
    {
        var stopwatch = Stopwatch.StartNew();
        while (stopwatch.Elapsed < timeout)
        {
            if (InspectorTransport.EnumerateListeningProcessIds().Contains(processId))
                return true;
            await Task.Delay(200, TestContext.Current.CancellationToken);
        }
        return false;
    }
}
