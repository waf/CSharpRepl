// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.IO;
using System.IO.Pipes;
using System.Net.Sockets;
using System.Runtime.Versioning;
using System.Threading;
using System.Threading.Tasks;

namespace CSharpRepl.Inspector.Contracts;

/// <summary>
/// The per-process, OS-access-controlled transport between controller and inspector: a named pipe on
/// Windows, a Unix domain socket elsewhere. Both endpoints are scoped to the current user, mirroring the
/// .NET diagnostic-port model — the security boundary is the connecting process's OS identity.
/// </summary>
/// <remarks>
/// M1 establishes the current-user baseline (<see cref="PipeOptions.CurrentUserOnly"/> on Windows; a 0700
/// user-owned directory on Unix). M6 adds the explicit DACL / peer-credential check on top.
/// </remarks>
public static class InspectorTransport
{
    public const int ProtocolVersion = 1;

    /// <summary>The Windows named-pipe name for a given target process id.</summary>
    public static string PipeName(int processId) => $"CSharpRepl.Inspector.{processId}";

    /// <summary>The Unix domain socket path for a given target process id (inside a 0700 user dir).</summary>
    public static string SocketPath(int processId) =>
        Path.Combine(SocketDirectory(), $"inspector-{processId}.sock");

    /// <summary>
    /// Connects to the inspector listening for <paramref name="processId"/>, retrying until the listener
    /// exists or <paramref name="timeout"/> elapses. Returns a duplex stream ready for a <see cref="MessageChannel"/>.
    /// </summary>
    public static async Task<Stream> ConnectAsync(int processId, TimeSpan timeout, CancellationToken cancellationToken)
    {
        if (OperatingSystem.IsWindows())
            return await ConnectWindowsAsync(processId, timeout, cancellationToken).ConfigureAwait(false);

        return await ConnectUnixAsync(processId, timeout, cancellationToken).ConfigureAwait(false);
    }

    [SupportedOSPlatform("windows")]
    private static async Task<Stream> ConnectWindowsAsync(int processId, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var client = new NamedPipeClientStream(
            serverName: ".",
            pipeName: PipeName(processId),
            direction: PipeDirection.InOut,
            options: PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        try
        {
            // ConnectAsync waits for the pipe to be created by the server, then connects.
            await client.ConnectAsync((int)timeout.TotalMilliseconds, cancellationToken).ConfigureAwait(false);
            return client;
        }
        catch
        {
            await client.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    private static async Task<Stream> ConnectUnixAsync(int processId, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var path = SocketPath(processId);
        var endpoint = new UnixDomainSocketEndPoint(path);
        var deadline = Environment.TickCount64 + (long)timeout.TotalMilliseconds;

        while (true)
        {
            var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
            try
            {
                await socket.ConnectAsync(endpoint, cancellationToken).ConfigureAwait(false);
                return new NetworkStream(socket, ownsSocket: true);
            }
            catch (SocketException) when (Environment.TickCount64 < deadline)
            {
                socket.Dispose();
                await Task.Delay(50, cancellationToken).ConfigureAwait(false); // listener not up yet
            }
            catch
            {
                socket.Dispose();
                throw;
            }
        }
    }

    private static string SocketDirectory()
    {
        // Prefer $XDG_RUNTIME_DIR (already user-private); otherwise a 0700 dir under the temp path.
        var runtimeDir = Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR");
        var baseDir = string.IsNullOrEmpty(runtimeDir) ? Path.GetTempPath() : runtimeDir;
        var dir = Path.Combine(baseDir, "csharprepl-inspector");
        if (!OperatingSystem.IsWindows())
            Directory.CreateDirectory(dir, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        else
            Directory.CreateDirectory(dir);
        return dir;
    }
}

/// <summary>
/// Server side of <see cref="InspectorTransport"/>: accepts controller connections for the current process.
/// Each <see cref="AcceptAsync"/> yields one connected duplex stream; the caller serves it then loops to
/// accept the next (supporting reconnect after <c>#disconnect</c>).
/// </summary>
public sealed class InspectorTransportListener : IDisposable
{
    private readonly int processId;
    private readonly Socket? unixListener; // non-null on Unix only
    private readonly string? unixSocketPath;

    public InspectorTransportListener(int processId)
    {
        this.processId = processId;
        if (OperatingSystem.IsWindows())
            return;

        unixSocketPath = InspectorTransport.SocketPath(processId);
        if (File.Exists(unixSocketPath))
            File.Delete(unixSocketPath); // remove a stale socket from a prior crashed run

        unixListener = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        unixListener.Bind(new UnixDomainSocketEndPoint(unixSocketPath));
        unixListener.Listen(backlog: 1);
    }

    public async Task<Stream> AcceptAsync(CancellationToken cancellationToken)
    {
        if (OperatingSystem.IsWindows())
            return await AcceptWindowsAsync(cancellationToken).ConfigureAwait(false);

        var connection = await unixListener!.AcceptAsync(cancellationToken).ConfigureAwait(false);
        return new NetworkStream(connection, ownsSocket: true);
    }

    [SupportedOSPlatform("windows")]
    private async Task<Stream> AcceptWindowsAsync(CancellationToken cancellationToken)
    {
        // A fresh server stream per connection; CurrentUserOnly applies a baseline current-user DACL.
        var server = new NamedPipeServerStream(
            pipeName: InspectorTransport.PipeName(processId),
            direction: PipeDirection.InOut,
            maxNumberOfServerInstances: 1,
            transmissionMode: PipeTransmissionMode.Byte,
            options: PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        try
        {
            await server.WaitForConnectionAsync(cancellationToken).ConfigureAwait(false);
            return server;
        }
        catch
        {
            await server.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    public void Dispose()
    {
        unixListener?.Dispose();
        if (unixSocketPath is not null && File.Exists(unixSocketPath))
        {
            try { File.Delete(unixSocketPath); } catch { /* best effort */ }
        }
    }
}
