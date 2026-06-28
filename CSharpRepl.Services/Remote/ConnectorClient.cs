// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using CSharpRepl.InjectedHook.Contracts;

namespace CSharpRepl.Services.Remote;

/// <summary>
/// Controller-side transport for a connector session: connects to the per-process pipe/socket, reads the
/// handshake, and exchanges framed <see cref="WireMessage"/>s with the connector. This is the thin wire
/// layer; <see cref="RemoteSession"/> adds the eval/disconnect semantics on top.
/// </summary>
public sealed class ConnectorClient : IAsyncDisposable
{
    private readonly Stream stream;
    private readonly MessageChannel channel;

    private ConnectorClient(Stream stream, MessageChannel channel, HandshakeMessage handshake)
    {
        this.stream = stream;
        this.channel = channel;
        Handshake = handshake;
    }

    /// <summary>Identity/version details the connector sent on connect.</summary>
    public HandshakeMessage Handshake { get; }

    /// <summary>
    /// Connects to the connector listening for <paramref name="processId"/> and consumes its handshake.
    /// Retries until the connector's listener exists or <paramref name="timeout"/> elapses.
    /// </summary>
    public static async Task<ConnectorClient> ConnectAsync(int processId, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var stream = await ConnectorTransport.ConnectAsync(processId, timeout, cancellationToken).ConfigureAwait(false);
        try
        {
            var channel = new MessageChannel(stream);
            var first = await channel.ReadAsync(cancellationToken).ConfigureAwait(false)
                ?? throw new IOException("The connector closed the connection before sending a handshake.");
            return first is not HandshakeMessage handshake
                ? throw new IOException($"Expected a handshake from the connector but received {first.GetType().Name}.")
                : new ConnectorClient(stream, channel, handshake);
        }
        catch
        {
            await stream.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    internal async Task<EvalResponse> EvalAsync(string code, bool detailed, CancellationToken cancellationToken)
    {
        await channel.WriteAsync(new EvalRequest { Code = code, Detailed = detailed }, CancellationToken.None).ConfigureAwait(false);

        // Don't cancel the read directly — abandoning the response the engine is about to send would desync the
        // channel. Instead, on cancellation send a CancelMessage (the engine cancels its evaluation token) and
        // keep reading until the (cancelled or completed) result arrives, leaving request/response in lock-step.
        await using var registration = cancellationToken.Register(static state =>
        {
            var ch = (MessageChannel)state!;
            try { _ = ch.WriteAsync(new CancelMessage(), CancellationToken.None); }
            catch { /* peer may be gone; the read below will surface it */ }
        }, channel);

        return await ReadResponseAsync<EvalResponse>("evaluation", "an evaluation result", CancellationToken.None).ConfigureAwait(false);
    }

    internal async Task<IReadOnlyList<string>> GetReferencePathsAsync(CancellationToken cancellationToken)
    {
        await channel.WriteAsync(new ReferencesRequest(), cancellationToken).ConfigureAwait(false);
        var response = await ReadResponseAsync<ReferencesResponse>("references request", "a references response", cancellationToken).ConfigureAwait(false);
        return response.Paths;
    }

    internal async Task<ReplaceResponse> ReplaceAsync(string targetMethod, string replacement, PatchMode mode, CancellationToken cancellationToken)
    {
        await channel.WriteAsync(new ReplaceRequest { TargetMethod = targetMethod, Replacement = replacement, Mode = mode }, cancellationToken).ConfigureAwait(false);
        return await ReadResponseAsync<ReplaceResponse>("replace request", "a replace response", cancellationToken).ConfigureAwait(false);
    }

    internal async Task<PatchListResponse> ListPatchesAsync(CancellationToken cancellationToken)
    {
        await channel.WriteAsync(new PatchListRequest(), cancellationToken).ConfigureAwait(false);
        return await ReadResponseAsync<PatchListResponse>("patch-list request", "a patch-list response", cancellationToken).ConfigureAwait(false);
    }

    internal async Task<RevertResponse> RevertAsync(int patchId, bool all, CancellationToken cancellationToken)
    {
        await channel.WriteAsync(new RevertRequest { PatchId = patchId, All = all }, cancellationToken).ConfigureAwait(false);
        return await ReadResponseAsync<RevertResponse>("revert request", "a revert response", cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Reads the next frame and asserts it's the expected response type, turning a closed connection or an
    /// unexpected message into a clear <see cref="IOException"/>. <paramref name="requestNoun"/> names the request
    /// in the connection-closed message; <paramref name="responseNoun"/> names the expected reply in the
    /// type-mismatch message (e.g. <c>"a revert response"</c>).
    /// </summary>
    private async Task<TResponse> ReadResponseAsync<TResponse>(string requestNoun, string responseNoun, CancellationToken cancellationToken)
        where TResponse : WireMessage
    {
        var response = await channel.ReadAsync(cancellationToken).ConfigureAwait(false)
            ?? throw new IOException($"The connector closed the connection without responding to the {requestNoun}.");
        return response as TResponse
            ?? throw new IOException($"Expected {responseNoun} from the connector but received {response.GetType().Name}.");
    }

    internal async Task SendDisconnectAsync(CancellationToken cancellationToken)
    {
        try
        {
            await channel.WriteAsync(new DisconnectMessage(), cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            // The peer may already be gone; disconnect is best-effort.
        }
    }

    public ValueTask DisposeAsync() => stream.DisposeAsync();
}
