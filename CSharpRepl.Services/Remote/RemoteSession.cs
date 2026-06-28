// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CSharpRepl.InjectedHook.Contracts;

namespace CSharpRepl.Services.Remote;

/// <summary>
/// A controller-side remote connector session: send a code line, receive its <see cref="EvalResponse"/>,
/// and end the session cleanly when the controller exits (leaving the target running). The REPL drives this
/// in place of the local <c>RoslynServices</c> when running in connect mode.
/// </summary>
public sealed class RemoteSession : IAsyncDisposable
{
    private readonly ConnectorClient client;
    private bool disconnected;

    private RemoteSession(ConnectorClient client) => this.client = client;

    /// <summary>Identity/version details the connector sent on connect (pid, runtime, DI-captured, ...).</summary>
    public HandshakeMessage Handshake => client.Handshake;

    public static async Task<RemoteSession> ConnectAsync(int processId, TimeSpan timeout, CancellationToken cancellationToken)
        => new RemoteSession(await ConnectorClient.ConnectAsync(processId, timeout, cancellationToken).ConfigureAwait(false));

    /// <summary>
    /// Evaluates a single submission in the target and returns its result. <paramref name="detailed"/> selects
    /// the detailed projection (object members included), matching the controller's detailed render level.
    /// </summary>
    public Task<EvalResponse> EvalAsync(string code, bool detailed, CancellationToken cancellationToken)
        => client.EvalAsync(code, detailed, cancellationToken);

    /// <summary>
    /// Fetches the target's loaded-assembly file paths so the controller can seed its remote editor workspace
    /// (IntelliSense + semantic highlighting) with the same references the engine compiles against.
    /// </summary>
    public Task<IReadOnlyList<string>> GetReferencePathsAsync(CancellationToken cancellationToken)
        => client.GetReferencePathsAsync(cancellationToken);

    /// <summary>
    /// Detours a live method in the target to a REPL-defined replacement. <paramref name="targetMethod"/> is a
    /// fully-qualified Type.Method; <paramref name="replacement"/> is an expression (usually a delegate the user
    /// defined) the engine evaluates against the shared state chain.
    /// </summary>
    public Task<ReplaceResponse> ReplaceAsync(string targetMethod, string replacement, PatchMode mode, CancellationToken cancellationToken)
        => client.ReplaceAsync(targetMethod, replacement, mode, cancellationToken);

    /// <summary>Lists the patches currently applied in the target by this session's engine.</summary>
    public Task<PatchListResponse> ListPatchesAsync(CancellationToken cancellationToken)
        => client.ListPatchesAsync(cancellationToken);

    /// <summary>Undoes a patch by id, or every patch when <paramref name="all"/> is true.</summary>
    public Task<RevertResponse> RevertAsync(int patchId, bool all, CancellationToken cancellationToken)
        => client.RevertAsync(patchId, all, cancellationToken);

    /// <summary>Asks the connector to end the session. The target process keeps running.</summary>
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
