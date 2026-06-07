// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using CSharpRepl.Inspector.Contracts;

namespace CSharpRepl.Inspector;

/// <summary>
/// Listens on the per-process transport for a controller, performs the handshake, and serves the request
/// loop (Eval / Disconnect) by forwarding to the <see cref="IInspectorEngine"/>. Runs on a background
/// thread so it never blocks the target's own work, and is written so a failed connection never tears down
/// the host — it just loops back to accept the next controller (supporting reconnect after #disconnect).
/// </summary>
internal static class InspectorServer
{
    // A non-secret, per-process instance id so a reconnecting controller can confirm it reached the same,
    // non-stale process instance. Not an authentication credential — the transport ACL is the real gate.
    private static readonly string InstanceId = Guid.NewGuid().ToString("N");

    public static void StartInBackground(IInspectorEngine engine)
    {
        var thread = new Thread(() => RunLoop(engine))
        {
            IsBackground = true,
            Name = "csharprepl-inspector",
        };
        thread.Start();
    }

    private static void RunLoop(IInspectorEngine engine)
    {
        var processId = Environment.ProcessId;
        try
        {
            using var listener = new InspectorTransportListener(processId);
            while (true)
            {
                Stream stream;
                try
                {
                    stream = listener.AcceptAsync(CancellationToken.None).GetAwaiter().GetResult();
                }
                catch
                {
                    return; // the listener itself is broken; give up but leave the host running
                }

                try
                {
                    using (stream)
                        ServeConnectionAsync(stream, engine, processId).GetAwaiter().GetResult();
                }
                catch
                {
                    // A single connection failed (peer closed, malformed frame, ...). Loop and accept again.
                }
            }
        }
        catch
        {
            // Never crash the host on inspector failure.
        }
    }

    private static async Task ServeConnectionAsync(Stream stream, IInspectorEngine engine, int processId)
    {
        var channel = new MessageChannel(stream);
        await channel.WriteAsync(BuildHandshake(processId), CancellationToken.None).ConfigureAwait(false);

        while (true)
        {
            var message = await channel.ReadAsync(CancellationToken.None).ConfigureAwait(false);
            if (message is null)
                return; // controller disconnected (EOF)

            switch (message)
            {
                case EvalRequest request:
                    var response = await EvaluateAsync(engine, request.Code, request.Detailed).ConfigureAwait(false);
                    await channel.WriteAsync(response, CancellationToken.None).ConfigureAwait(false);
                    break;

                case ReferencesRequest:
                    var references = await GetReferencePathsAsync(engine).ConfigureAwait(false);
                    await channel.WriteAsync(new ReferencesResponse { Paths = references }, CancellationToken.None).ConfigureAwait(false);
                    break;

                case DisconnectMessage:
                    return; // graceful disconnect; the target keeps running

                default:
                    // Unknown/unsupported message — ignore defensively rather than tearing down the session.
                    break;
            }
        }
    }

    private static async Task<EvalResponse> EvaluateAsync(IInspectorEngine engine, string code, bool detailed)
    {
        try
        {
            return await engine.EvalAsync(code, detailed, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            // The engine projects compile/runtime errors itself; this catches only unexpected engine faults.
            return EvalResponse.FromException(
                new RemoteException
                {
                    TypeName = exception.GetType().FullName ?? exception.GetType().Name,
                    Message = exception.Message,
                    Detail = exception.ToString(),
                },
                committed: false);
        }
    }

    private static async Task<IReadOnlyList<string>> GetReferencePathsAsync(IInspectorEngine engine)
    {
        try
        {
            return await engine.GetReferencePathsAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch
        {
            // The controller degrades to a reference-less editor workspace if this fails; never tear down the session.
            return [];
        }
    }

    private static HandshakeMessage BuildHandshake(int processId) => new()
    {
        ProcessId = processId,
        ProcessName = SafeProcessName(),
        RuntimeVersion = RuntimeInformation.FrameworkDescription,
        InspectorVersion = typeof(InspectorServer).Assembly.GetName().Version?.ToString() ?? "0.0.0",
        ProtocolVersion = InspectorTransport.ProtocolVersion,
        DiProviderCaptured = InspectorRoots.Services is not null,
        AssemblyAvailability = DetectAssemblyAvailability(),
        SessionId = $"{processId}-{InstanceId}",
    };

    /// <summary>
    /// Classifies the target's launch so the controller can refuse or warn. A self-contained single-file
    /// bundle has even corlib in-memory (empty Location); a framework-dependent single-file bundle keeps the
    /// shared framework on disk but bundles the app's own assemblies.
    /// </summary>
    private static TargetAssemblyAvailability DetectAssemblyAvailability()
    {
        try
        {
            if (string.IsNullOrEmpty(typeof(object).Assembly.Location))
                return TargetAssemblyAvailability.SelfContainedSingleFile;

            var entryAssembly = Assembly.GetEntryAssembly();
            if (entryAssembly is not null && string.IsNullOrEmpty(entryAssembly.Location))
                return TargetAssemblyAvailability.FrameworkDependentSingleFile;

            return TargetAssemblyAvailability.Normal;
        }
        catch
        {
            return TargetAssemblyAvailability.Normal;
        }
    }

    private static string SafeProcessName()
    {
        try { return Process.GetCurrentProcess().ProcessName; }
        catch { return "unknown"; }
    }
}
