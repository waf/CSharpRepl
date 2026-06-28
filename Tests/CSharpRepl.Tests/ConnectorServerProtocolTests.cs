// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

#nullable enable

using System;
using System.Buffers.Binary;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CSharpRepl.InjectedHook.Contracts;
using Xunit;

namespace CSharpRepl.Tests;

/// <summary>
/// Drives the injected <c>ConnectorServer</c> at the raw wire-protocol level (a hand-rolled MessageChannel
/// instead of ConnectorClient) against a real hooked child process, covering server behaviors a well-behaved
/// controller never triggers: a stray cancel with no evaluation in flight, a malformed frame tearing down
/// only that one connection (the accept loop then serves a fresh controller on the same live instance), and
/// the per-process-instance session id surviving across connections.
/// </summary>
public class ConnectorServerProtocolTests
{
    [Fact(Timeout = 120_000)]
    public async Task Server_SurvivesProtocolErrors_AndKeepsServingNewConnections()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var process = ConnectorTestSupport.StartHookedTarget();

        // Drain the child's output so a full pipe buffer can't block it; results are only needed for diagnostics.
        var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);

        try
        {
            // --- First connection: handshake fields, a stray cancel, then a normal evaluation ---
            await using var stream = await ConnectorTransport.ConnectAsync(process.Id, TimeSpan.FromSeconds(30), cancellationToken);
            var channel = new MessageChannel(stream);
            var handshake = Assert.IsType<HandshakeMessage>(await channel.ReadAsync(cancellationToken));
            Assert.Equal(ConnectorTransport.ProtocolVersion, handshake.ProtocolVersion);
            Assert.NotEmpty(handshake.ConnectorVersion);
            Assert.NotEmpty(handshake.RuntimeVersion);
            Assert.NotEmpty(handshake.ProcessName);

            // A cancel with nothing in flight must be ignored — not desync the request/response loop.
            await channel.WriteAsync(new CancelMessage(), cancellationToken);
            await channel.WriteAsync(new EvalRequest { Code = "6 * 7" }, cancellationToken);
            var result = Assert.IsType<EvalResponse>(await channel.ReadAsync(cancellationToken));
            Assert.Equal(ResultKind.Value, result.Kind);
            Assert.Equal("42", result.Value!.DisplayText);

            // The references branch works at the raw protocol level too.
            await channel.WriteAsync(new ReferencesRequest(), cancellationToken);
            var references = Assert.IsType<ReferencesResponse>(await channel.ReadAsync(cancellationToken));
            Assert.Contains(references.Paths,
                p => p.EndsWith("CSharpRepl.InjectedHook.TestTarget.dll", StringComparison.OrdinalIgnoreCase));

            // --- A malformed frame (valid length prefix, garbage body) kills only this connection ---
            var garbage = Encoding.UTF8.GetBytes("never-json");
            var frame = new byte[4 + garbage.Length];
            BinaryPrimitives.WriteInt32LittleEndian(frame, garbage.Length);
            garbage.CopyTo(frame, 4);
            await stream.WriteAsync(frame, cancellationToken);
            await stream.FlushAsync(cancellationToken);

            // The server drops us rather than responding: the next read observes the closure (a clean EOF or
            // a broken-pipe error, depending on how the OS surfaces the server-side dispose).
            WireMessage? afterGarbage = null;
            Exception? dropError = null;
            try { afterGarbage = await channel.ReadAsync(cancellationToken); }
            catch (Exception ex) { dropError = ex; }
            Assert.True(afterGarbage is null, $"Expected the server to drop the connection but it responded with {afterGarbage?.GetType().Name}.");
            Assert.True(dropError is null or IOException or EndOfStreamException,
                $"Expected a closed connection, got: {dropError?.GetType().Name}");

            // --- The accept loop recovers: a fresh controller is served by the same live process instance ---
            await using var secondStream = await ConnectorTransport.ConnectAsync(process.Id, TimeSpan.FromSeconds(30), cancellationToken);
            var secondChannel = new MessageChannel(secondStream);
            var secondHandshake = Assert.IsType<HandshakeMessage>(await secondChannel.ReadAsync(cancellationToken));
            Assert.Equal(handshake.SessionId, secondHandshake.SessionId); // confirms a non-stale, same-instance reconnect

            // The engine's submission chain also survived the dropped connection.
            await secondChannel.WriteAsync(new EvalRequest { Code = "\"still alive\"" }, cancellationToken);
            var revived = Assert.IsType<EvalResponse>(await secondChannel.ReadAsync(cancellationToken));
            Assert.Equal("\"still alive\"", revived.Value!.DisplayText);

            await secondChannel.WriteAsync(new DisconnectMessage(), cancellationToken);
            Assert.False(process.HasExited, "The target should keep running through protocol errors and disconnects.");
        }
        finally
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
            try { await Task.WhenAll(stdoutTask, stderrTask); } catch { /* output drained on process exit */ }
        }
    }
}
