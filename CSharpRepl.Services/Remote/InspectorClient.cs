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
/// Controller-side transport for an inspector session: connects to the per-process pipe/socket, reads the
/// handshake, and exchanges framed <see cref="WireMessage"/>s with the inspector. This is the thin wire
/// layer; <see cref="RemoteSession"/> adds the eval/disconnect semantics on top.
/// </summary>
public sealed class InspectorClient : IAsyncDisposable
{
    private readonly Stream stream;
    private readonly MessageChannel channel;

    private InspectorClient(Stream stream, MessageChannel channel, HandshakeMessage handshake)
    {
        this.stream = stream;
        this.channel = channel;
        Handshake = handshake;
    }

    /// <summary>Identity/version details the inspector sent on connect.</summary>
    public HandshakeMessage Handshake { get; }

    /// <summary>
    /// Connects to the inspector listening for <paramref name="processId"/> and consumes its handshake.
    /// Retries until the inspector's listener exists or <paramref name="timeout"/> elapses.
    /// </summary>
    public static async Task<InspectorClient> ConnectAsync(int processId, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var stream = await InspectorTransport.ConnectAsync(processId, timeout, cancellationToken).ConfigureAwait(false);
        try
        {
            var channel = new MessageChannel(stream);
            var first = await channel.ReadAsync(cancellationToken).ConfigureAwait(false)
                ?? throw new IOException("The inspector closed the connection before sending a handshake.");
            return first is not HandshakeMessage handshake
                ? throw new IOException($"Expected a handshake from the inspector but received {first.GetType().Name}.")
                : new InspectorClient(stream, channel, handshake);
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

        var response = await channel.ReadAsync(CancellationToken.None).ConfigureAwait(false)
            ?? throw new IOException("The inspector closed the connection without responding to the evaluation.");
        return response is not EvalResponse evalResponse
            ? throw new IOException($"Expected an evaluation result from the inspector but received {response.GetType().Name}.")
            : evalResponse;
    }

    internal async Task<IReadOnlyList<string>> GetReferencePathsAsync(CancellationToken cancellationToken)
    {
        await channel.WriteAsync(new ReferencesRequest(), cancellationToken).ConfigureAwait(false);

        var response = await channel.ReadAsync(cancellationToken).ConfigureAwait(false)
            ?? throw new IOException("The inspector closed the connection without responding to the references request.");
        return response is not ReferencesResponse referencesResponse
            ? throw new IOException($"Expected a references response from the inspector but received {response.GetType().Name}.")
            : referencesResponse.Paths;
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
