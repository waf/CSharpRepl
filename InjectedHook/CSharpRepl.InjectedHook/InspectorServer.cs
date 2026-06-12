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
using CSharpRepl.InjectedHook.Contracts;

namespace CSharpRepl.InjectedHook;

/// <summary>
/// Listens on the per-process transport, performs the handshake, and serves the request loop by forwarding
/// to the IInspectorEngine.
///
/// - Runs on a background thread so it never blocks the target's own work.
/// - A failed connection never tears down the host — it loops back to accept the next controller
///   (supporting reconnect after the controller detaches).
/// </summary>
internal static class InspectorServer
{
    // A non-secret, per-process instance id so a reconnecting controller can confirm it reached the same,
    // non-stale process instance. Not an authentication credential — the transport ACL is the real gate.
    private static readonly string InstanceId = Guid.NewGuid().ToString("N");

    public static void StartInBackground(IInspectorEngine engine)
    {
        // use a background thread so the inspector loop never keeps the target alive. Don't use a task because we don't want to consume the target's thread pool with a long-running loop.
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
                    {
                        ServeConnectionAsync(stream, engine, processId).GetAwaiter().GetResult();
                    }
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

        // The loop carries the "next message to process": an evaluation reads ahead (concurrently with running)
        // so it can observe a CancelMessage, and hands back whatever it read for the next iteration.
        var message = await channel.ReadAsync(CancellationToken.None).ConfigureAwait(false);
        while (message is not null)
        {
            switch (message)
            {
                case EvalRequest request:
                    message = await EvaluateWithCancellationAsync(channel, engine, request).ConfigureAwait(false);
                    break;

                case ReferencesRequest:
                    var references = await GetReferencePathsAsync(engine).ConfigureAwait(false);
                    await channel.WriteAsync(new ReferencesResponse { Paths = references }, CancellationToken.None).ConfigureAwait(false);
                    message = await channel.ReadAsync(CancellationToken.None).ConfigureAwait(false);
                    break;

                case DisconnectMessage:
                    return; // graceful disconnect; the target keeps running

                case CancelMessage:
                    // A cancel for an evaluation that already finished — nothing to cancel. Ignore and read on.
                    message = await channel.ReadAsync(CancellationToken.None).ConfigureAwait(false);
                    break;

                default:
                    // Unknown/unsupported message — ignore defensively rather than tearing down the session.
                    message = await channel.ReadAsync(CancellationToken.None).ConfigureAwait(false);
                    break;
            }
        }
    }

    /// <summary>
    /// Evaluates a submission while concurrently reading the channel, so a CancelMessage arriving
    /// mid-evaluation cancels the engine's token.
    ///
    /// - Returns the next message to process (the one read ahead), or null on EOF.
    /// - Keeps framing in lock-step: exactly one response is written per request.
    /// </summary>
    private static async Task<WireMessage?> EvaluateWithCancellationAsync(MessageChannel channel, IInspectorEngine engine, EvalRequest request)
    {
        using var cts = new CancellationTokenSource();
        // Run the evaluation on the thread pool so the concurrent read below starts immediately: Roslyn can run
        // a submission inline on the calling thread (a CPU-bound or sleeping submission would otherwise block us
        // from ever reading the CancelMessage).
        var evalTask = Task.Run(() => EvaluateAsync(engine, request.Code, request.Detailed, cts.Token));
        var readTask = channel.ReadAsync(CancellationToken.None);

        var completed = await Task.WhenAny(evalTask, readTask).ConfigureAwait(false);
        if (completed == evalTask)
        {
            // Evaluation finished first; send its result, then surface the read we already had in flight as the
            // next message (it completes when the controller sends its next request/cancel/disconnect).
            await channel.WriteAsync(await evalTask.ConfigureAwait(false), CancellationToken.None).ConfigureAwait(false);
            return await readTask.ConfigureAwait(false);
        }

        var incoming = await readTask.ConfigureAwait(false);
        if (incoming is null)
        {
            // Controller disconnected mid-evaluation; cancel and let the evaluation unwind, then stop.
            cts.Cancel();
            try { await evalTask.ConfigureAwait(false); } catch { /* unwinding a cancelled/faulted eval */ }
            return null;
        }

        if (incoming is CancelMessage)
        {
            cts.Cancel();
        }

        // Whether cancelled or a stray message arrived, finish the evaluation and write exactly one response.
        await channel.WriteAsync(await evalTask.ConfigureAwait(false), CancellationToken.None).ConfigureAwait(false);

        // A cancel is consumed here; any other message is processed next. After a cancel, read the next message.
        return incoming is CancelMessage
            ? await channel.ReadAsync(CancellationToken.None).ConfigureAwait(false)
            : incoming;
    }

    private static async Task<EvalResponse> EvaluateAsync(IInspectorEngine engine, string code, bool detailed, CancellationToken cancellationToken)
    {
        try
        {
            return await engine.EvalAsync(code, detailed, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Cooperative cancellation reached a Roslyn-observed point; report it as a (non-committed) result so
            // the controller renders it and the session continues.
            return EvalResponse.FromException(
                new RemoteException { TypeName = "OperationCanceledException", Message = "Evaluation was canceled in the target." },
                committed: false);
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
    /// Classifies the target's launch so the controller can refuse or warn.
    ///
    /// - Self-contained single-file: even corlib is in-memory (empty Location).
    /// - Framework-dependent single-file: shared framework on disk, but the app's own assemblies are bundled.
    /// </summary>
    private static TargetAssemblyAvailability DetectAssemblyAvailability()
    {
        try
        {
            if (string.IsNullOrEmpty(typeof(object).Assembly.Location))
            {
                return TargetAssemblyAvailability.SelfContainedSingleFile;
            }

            var entryAssembly = Assembly.GetEntryAssembly();
            if (entryAssembly is not null && string.IsNullOrEmpty(entryAssembly.Location))
            {
                return TargetAssemblyAvailability.FrameworkDependentSingleFile;
            }

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
