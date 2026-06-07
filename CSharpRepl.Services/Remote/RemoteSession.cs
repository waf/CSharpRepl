// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CSharpRepl.Inspector.Contracts;

namespace CSharpRepl.Services.Remote;

/// <summary>
/// A controller-side remote inspector session: send a code line, receive its <see cref="EvalResponse"/>,
/// and end the session cleanly with <c>#disconnect</c> (leaving the target running). The REPL drives this
/// in place of the local <c>RoslynServices</c> when running in inspect mode.
/// </summary>
/// <remarks>
/// Rendering an <see cref="EvalResponse"/> through the existing PrettyPrinter is M3; M1 exposes the
/// response directly so the round-trip can be observed and tested.
/// </remarks>
public sealed class RemoteSession : IAsyncDisposable
{
    private readonly InspectorClient client;
    private bool disconnected;

    private RemoteSession(InspectorClient client) => this.client = client;

    /// <summary>Identity/version details the inspector sent on connect (pid, runtime, DI-captured, ...).</summary>
    public HandshakeMessage Handshake => client.Handshake;

    public static async Task<RemoteSession> ConnectAsync(int processId, TimeSpan timeout, CancellationToken cancellationToken)
        => new RemoteSession(await InspectorClient.ConnectAsync(processId, timeout, cancellationToken).ConfigureAwait(false));

    /// <summary>
    /// Evaluates a single submission in the target and returns its result. <paramref name="detailed"/> selects
    /// the detailed projection (object members included), matching the controller's detailed render level.
    /// </summary>
    public Task<EvalResponse> EvalAsync(string code, bool detailed, CancellationToken cancellationToken)
        => client.EvalAsync(code, detailed, cancellationToken);

    /// <summary>
    /// Fetches the target's loaded-assembly file paths so the controller can seed its remote editor workspace
    /// (IntelliSense + semantic highlighting) with the same references the engine compiles against (M5).
    /// </summary>
    public Task<IReadOnlyList<string>> GetReferencePathsAsync(CancellationToken cancellationToken)
        => client.GetReferencePathsAsync(cancellationToken);

    /// <summary>Asks the inspector to end the session. The target process keeps running.</summary>
    public async Task DisconnectAsync(CancellationToken cancellationToken)
    {
        if (disconnected) return;
        disconnected = true;
        await client.SendDisconnectAsync(cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        await DisconnectAsync(CancellationToken.None).ConfigureAwait(false);
        await client.DisposeAsync().ConfigureAwait(false);
    }
}
