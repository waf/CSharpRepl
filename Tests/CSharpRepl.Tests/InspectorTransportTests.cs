// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

#nullable enable

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using CSharpRepl.InjectedHook.Contracts;
using Xunit;

namespace CSharpRepl.Tests;

/// <summary>
/// Integration tests for the inspector's wire layer over the real OS transport (a named pipe on Windows, a
/// Unix domain socket elsewhere): listener/client connection with the production security options,
/// length-prefixed polymorphic JSON framing, write serialization, reconnect after a controller leaves, and
/// the defensive handling of torn/oversized/malformed frames from a buggy or hostile peer.
///
/// The Windows pipe is created with zero-byte buffers, so a write rendezvouses with the peer's read; like the
/// production server/controller loops, these tests always have the read pending while the write is in flight.
/// </summary>
public class InspectorTransportTests
{
    // Each test gets its own endpoint: pipe/socket names are keyed by "process id", so derive unique fake ids
    // from this process's real pid to avoid colliding with concurrent tests or a genuinely hooked process.
    private static int FakeProcessId(int salt) => Environment.ProcessId * 16 + salt;

    [Fact(Timeout = 60_000)]
    public async Task TransportAndChannel_RoundTripMessages_AndAcceptAReconnect()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var processId = FakeProcessId(1);
        using var listener = new InspectorTransportListener(processId);

        var (clientStream, serverStream) = await ConnectPairAsync(listener, processId, cancellationToken);
        await using (clientStream)
        await using (serverStream)
        {
            var clientChannel = new MessageChannel(clientStream);
            var serverChannel = new MessageChannel(serverStream);

            // Server → client: the handshake survives the polymorphic ($kind) round-trip with its enums intact.
            var handshake = Assert.IsType<HandshakeMessage>(await ExchangeAsync(serverChannel, clientChannel, new HandshakeMessage
            {
                ProcessId = processId,
                ProcessName = "transport-test",
                ProtocolVersion = InspectorTransport.ProtocolVersion,
                AssemblyAvailability = TargetAssemblyAvailability.FrameworkDependentSingleFile,
                SessionId = "session-1",
            }, cancellationToken));
            Assert.Equal(processId, handshake.ProcessId);
            Assert.Equal("transport-test", handshake.ProcessName);
            Assert.Equal(TargetAssemblyAvailability.FrameworkDependentSingleFile, handshake.AssemblyAvailability);
            Assert.Equal("session-1", handshake.SessionId);

            // Client → server and a structured response back: a nested RemoteValue tree round-trips intact.
            var request = Assert.IsType<EvalRequest>(await ExchangeAsync(
                clientChannel, serverChannel, new EvalRequest { Code = "1 + 1", Detailed = true }, cancellationToken));
            Assert.Equal("1 + 1", request.Code);
            Assert.True(request.Detailed);

            var response = Assert.IsType<EvalResponse>(await ExchangeAsync(serverChannel, clientChannel, EvalResponse.FromValue(new RemoteValue
            {
                Kind = RemoteValueKind.Collection,
                TypeName = "List<int>",
                Count = 2,
                Items =
                [
                    new RemoteValue { Kind = RemoteValueKind.Scalar, TypeName = "int", DisplayText = "1", Style = RemoteValueStyle.Number },
                    new RemoteValue
                    {
                        Kind = RemoteValueKind.Object,
                        TypeName = "Service",
                        DisplayText = "Service",
                        Style = RemoteValueStyle.TypeName,
                        Members = [new RemoteMember { Name = "Value", Value = new RemoteValue { Kind = RemoteValueKind.Scalar, DisplayText = "41" } }],
                    },
                ],
            }, committed: true), cancellationToken));
            Assert.True(response.Committed);
            Assert.Equal(ResultKind.Value, response.Kind);
            Assert.Equal(RemoteValueKind.Collection, response.Value!.Kind);
            Assert.Equal(2, response.Value.Count);
            Assert.Equal(RemoteValueStyle.Number, response.Value.Items![0].Style);
            var member = Assert.Single(response.Value.Items[1].Members!);
            Assert.Equal("Value", member.Name);
            Assert.Equal("41", member.Value.DisplayText);

            // Concurrent writers on one channel: the write gate must keep frames un-interleaved over the real
            // pipe (the production race is an out-of-band CancelMessage during the next EvalRequest). The
            // server reads while the writers run, like the real request loop.
            const int messagesPerWriter = 25;
            var writers = Task.WhenAll(
                Task.Run(() => WriteManyAsync(clientChannel, "a", messagesPerWriter), cancellationToken),
                Task.Run(() => WriteManyAsync(clientChannel, "b", messagesPerWriter), cancellationToken));
            var received = new List<string>();
            for (var i = 0; i < messagesPerWriter * 2; i++)
            {
                received.Add(Assert.IsType<EvalRequest>(await serverChannel.ReadAsync(cancellationToken)).Code);
            }
            await writers;
            Assert.Equal(
                Enumerable.Range(0, messagesPerWriter).SelectMany(i => new[] { $"a{i}", $"b{i}" }).Order(),
                received.Order());

            // A clean client close reads as EOF (null) — the signal the server treats as a disconnect.
            await clientStream.DisposeAsync();
            Assert.Null(await serverChannel.ReadAsync(cancellationToken));
        }

        // The listener then accepts a brand-new controller on the same endpoint (reconnect support).
        var (secondClient, secondServer) = await ConnectPairAsync(listener, processId, cancellationToken);
        await using (secondClient)
        await using (secondServer)
        {
            Assert.IsType<DisconnectMessage>(await ExchangeAsync(
                new MessageChannel(secondClient), new MessageChannel(secondServer), new DisconnectMessage(), cancellationToken));
        }

        static async Task WriteManyAsync(MessageChannel channel, string prefix, int count)
        {
            for (var i = 0; i < count; i++)
            {
                await channel.WriteAsync(new EvalRequest { Code = $"{prefix}{i}" }, CancellationToken.None);
            }
        }
    }

    [Fact(Timeout = 60_000)]
    public async Task Channel_RejectsTornOversizedAndMalformedFrames()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var processId = FakeProcessId(2);
        using var listener = new InspectorTransportListener(processId);

        // Torn frame: the header promises 16 bytes but the peer vanishes after 4 — never silently truncated.
        {
            var (clientStream, serverStream) = await ConnectPairAsync(listener, processId, cancellationToken);
            await using var server = serverStream;
            var readTask = new MessageChannel(serverStream).ReadAsync(cancellationToken);
            await clientStream.WriteAsync(new byte[] { 16, 0, 0, 0, 1, 2, 3, 4 }, cancellationToken);
            await clientStream.FlushAsync(cancellationToken);
            await clientStream.DisposeAsync();
            await Assert.ThrowsAsync<EndOfStreamException>(() => readTask);
        }

        // Oversized frame: a hostile length prefix is rejected up front, before any payload is allocated.
        {
            var (clientStream, serverStream) = await ConnectPairAsync(listener, processId, cancellationToken);
            await using var client = clientStream;
            await using var server = serverStream;
            var readTask = new MessageChannel(serverStream).ReadAsync(cancellationToken);
            await clientStream.WriteAsync(FrameHeader(65 * 1024 * 1024), cancellationToken);
            await clientStream.FlushAsync(cancellationToken);
            await Assert.ThrowsAsync<InvalidDataException>(() => readTask);
        }

        // Negative length prefix: same defensive rejection.
        {
            var (clientStream, serverStream) = await ConnectPairAsync(listener, processId, cancellationToken);
            await using var client = clientStream;
            await using var server = serverStream;
            var readTask = new MessageChannel(serverStream).ReadAsync(cancellationToken);
            await clientStream.WriteAsync(FrameHeader(-1), cancellationToken);
            await clientStream.FlushAsync(cancellationToken);
            await Assert.ThrowsAsync<InvalidDataException>(() => readTask);
        }

        // Malformed body: the framing is valid but the payload isn't a WireMessage — surfaces as an exception
        // the server's per-connection handler can catch, rather than crashing the host.
        {
            var (clientStream, serverStream) = await ConnectPairAsync(listener, processId, cancellationToken);
            await using var client = clientStream;
            await using var server = serverStream;
            var readTask = new MessageChannel(serverStream).ReadAsync(cancellationToken);
            var body = Encoding.UTF8.GetBytes("this is not json");
            await clientStream.WriteAsync(FrameHeader(body.Length), cancellationToken);
            await clientStream.WriteAsync(body, cancellationToken);
            await clientStream.FlushAsync(cancellationToken);
            await Assert.ThrowsAnyAsync<JsonException>(() => readTask);
        }

        static byte[] FrameHeader(int declaredLength)
        {
            var header = new byte[4];
            BinaryPrimitives.WriteInt32LittleEndian(header, declaredLength);
            return header;
        }
    }

    [Fact(Timeout = 60_000)]
    public async Task Connect_TimesOut_WhenNoInspectorIsListening()
    {
        var exception = await Record.ExceptionAsync(() => InspectorTransport.ConnectAsync(
            FakeProcessId(3), TimeSpan.FromMilliseconds(300), TestContext.Current.CancellationToken));

        // TimeoutException from the Windows named pipe; SocketException once the Unix retry deadline elapses.
        Assert.True(exception is TimeoutException or SocketException,
            $"Expected a connect timeout, got: {exception?.GetType().Name ?? "no exception"}");
    }

    /// <summary>Writes on one channel while the peer's read is already pending, and returns what the peer read.</summary>
    private static async Task<WireMessage?> ExchangeAsync(
        MessageChannel from, MessageChannel to, WireMessage message, CancellationToken cancellationToken)
    {
        var readTask = to.ReadAsync(cancellationToken);
        await from.WriteAsync(message, cancellationToken);
        return await readTask;
    }

    private static async Task<(Stream Client, Stream Server)> ConnectPairAsync(
        InspectorTransportListener listener, int processId, CancellationToken cancellationToken)
    {
        var acceptTask = listener.AcceptAsync(cancellationToken);
        var client = await InspectorTransport.ConnectAsync(processId, TimeSpan.FromSeconds(30), cancellationToken);
        return (client, await acceptTask);
    }
}
