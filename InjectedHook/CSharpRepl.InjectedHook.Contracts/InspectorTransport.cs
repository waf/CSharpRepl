// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Pipes;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Threading;
using System.Threading.Tasks;

namespace CSharpRepl.InjectedHook.Contracts;

/// <summary>
/// The per-process, OS-access-controlled transport between controller and inspector: a named pipe on
/// Windows, a Unix domain socket elsewhere.
///
/// - Current-user only, mirroring the .NET diagnostic-port model: an explicit DACL granting just the current
///   user's SID on Windows, a 0700 user-owned directory plus a peer-credential check on Unix.
/// </summary>
public static class InspectorTransport
{
    public const int ProtocolVersion = 1;

    // The endpoint name embeds the target's process id. These are the single source of truth for that
    // convention: PipeName/SocketPath build it, and TryParseProcessId/EnumerateListeningProcessIds reverse it
    // for discovery (`inspect list`). Keep them in sync.
    private const string PipeNamePrefix = "CSharpRepl.InjectedHook.";
    private const string SocketNamePrefix = "inspector-";
    private const string SocketNameSuffix = ".sock";

    /// <summary>The Windows named-pipe name for a given target process id.</summary>
    public static string PipeName(int processId) => PipeNamePrefix + processId;

    /// <summary>The Unix domain socket path for a given target process id (inside a 0700 user dir).</summary>
    public static string SocketPath(int processId) =>
        Path.Combine(SocketDirectory(), SocketNamePrefix + processId + SocketNameSuffix);

    /// <summary>
    /// Discovers the process ids of inspector-enabled processes for the current user by scanning the
    /// transport namespace for our PID-embedded endpoints.
    ///
    /// - Windows: named pipes under <c>\\.\pipe\</c> matching <see cref="PipeName(int)"/>.
    /// - Unix: socket files in the user-private socket directory matching <see cref="SocketPath(int)"/>.
    ///
    /// Best-effort and never throws. A returned id may still be stale (a crashed Unix process can leave 
    /// its socket behind), so the caller should confirm liveness.
    /// </summary>
    public static IReadOnlyList<int> EnumerateListeningProcessIds()
    {
        var processIds = new HashSet<int>();
        try
        {
            if (OperatingSystem.IsWindows())
            {
                const string PipeRoot = @"\\.\pipe\";
                foreach (var pipe in Directory.GetFiles(PipeRoot))
                {
                    // Directory.GetFiles returns full paths like \\.\pipe\CSharpRepl.InjectedHook.1234.
                    var name = pipe.StartsWith(PipeRoot, StringComparison.Ordinal) ? pipe[PipeRoot.Length..] : pipe;
                    if (TryParseProcessId(name, out var processId)) processIds.Add(processId);
                }
            }
            else
            {
                var directory = SocketDirectory();
                if (Directory.Exists(directory))
                {
                    foreach (var socket in Directory.GetFiles(directory, SocketNamePrefix + "*" + SocketNameSuffix))
                    {
                        if (TryParseProcessId(Path.GetFileName(socket), out var processId)) processIds.Add(processId);
                    }
                }
            }
        }
        catch
        {
            // Discovery is advisory; a failure to enumerate the namespace just yields what we found so far.
        }

        var sorted = new List<int>(processIds);
        sorted.Sort();
        return sorted;
    }

    /// <summary>
    /// Extracts the process id from a transport endpoint name (a Windows pipe name or a Unix socket file
    /// name), reversing <see cref="PipeName(int)"/> / <see cref="SocketPath(int)"/>.
    /// </summary>
    public static bool TryParseProcessId(string endpointName, out int processId)
    {
        processId = 0;
        if (string.IsNullOrEmpty(endpointName)) return false;

        string number;
        if (endpointName.StartsWith(PipeNamePrefix, StringComparison.Ordinal))
        {
            number = endpointName[PipeNamePrefix.Length..];
        }
        else if (endpointName.StartsWith(SocketNamePrefix, StringComparison.Ordinal) &&
                 endpointName.EndsWith(SocketNameSuffix, StringComparison.Ordinal))
        {
            number = endpointName[SocketNamePrefix.Length..^SocketNameSuffix.Length];
        }
        else
        {
            return false;
        }

        // NumberStyles.None: digits only — reject a leading sign, whitespace, or thousands separators.
        return int.TryParse(number, NumberStyles.None, CultureInfo.InvariantCulture, out processId) && processId > 0;
    }

    /// <summary>
    /// Connects to the inspector listening for processId, retrying until the listener exists or the timeout
    /// elapses. Returns a duplex stream ready for a MessageChannel.
    /// </summary>
    public static async Task<Stream> ConnectAsync(int processId, TimeSpan timeout, CancellationToken cancellationToken)
    {
        if (OperatingSystem.IsWindows())
        {
            return await ConnectWindowsAsync(processId, timeout, cancellationToken).ConfigureAwait(false);
        }

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
        {
            Directory.CreateDirectory(dir, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
        else
        {
            Directory.CreateDirectory(dir);
        }

        return dir;
    }
}

/// <summary>
/// Server side of the transport: accepts controller connections for the current process. Each AcceptAsync
/// yields one connected duplex stream; the caller serves it then loops to accept the next (reconnect support).
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
        {
            return;
        }

        unixSocketPath = InspectorTransport.SocketPath(processId);
        if (File.Exists(unixSocketPath))
        {
            File.Delete(unixSocketPath); // remove a stale socket from a prior crashed run
        }

        unixListener = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        unixListener.Bind(new UnixDomainSocketEndPoint(unixSocketPath));
        unixListener.Listen(backlog: 1);
    }

    public async Task<Stream> AcceptAsync(CancellationToken cancellationToken)
    {
        if (OperatingSystem.IsWindows())
        {
            return await AcceptWindowsAsync(cancellationToken).ConfigureAwait(false);
        }

        // Unix: the socket already lives in a 0700 user-owned directory, but assert the connecting peer is the
        // same user as a second, real check (the socket file's own mode is only forced to 0600 in .NET 11+).
        // A peer from another user is rejected and we loop to accept the next connection.
        while (true)
        {
            var connection = await unixListener!.AcceptAsync(cancellationToken).ConfigureAwait(false);
            if (UnixPeerCredentials.IsSameUser(connection))
            {
                return new NetworkStream(connection, ownsSocket: true);
            }

            try { connection.Dispose(); } catch { /* best effort */ }
        }
    }

    [SupportedOSPlatform("windows")]
    private async Task<Stream> AcceptWindowsAsync(CancellationToken cancellationToken)
    {
        // A fresh server stream per connection, locked down with an explicit DACL that grants ONLY the current
        // user's logon SID — the default pipe DACL grants Everyone read, so we never rely on it. This both
        // scopes access to the current user and serves as the Windows peer check (a cross-user client is denied
        // at connect). We therefore drop PipeOptions.CurrentUserOnly, which can't be combined with a custom ACL.
        var server = NamedPipeServerStreamAcl.Create(
            pipeName: InspectorTransport.PipeName(processId),
            direction: PipeDirection.InOut,
            maxNumberOfServerInstances: 1,
            transmissionMode: PipeTransmissionMode.Byte,
            options: PipeOptions.Asynchronous,
            inBufferSize: 0,
            outBufferSize: 0,
            pipeSecurity: CurrentUserOnlyPipeSecurity());
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

    [SupportedOSPlatform("windows")]
    private static PipeSecurity CurrentUserOnlyPipeSecurity()
    {
        var security = new PipeSecurity();
        using var identity = WindowsIdentity.GetCurrent();
        var user = identity.User
            ?? throw new InvalidOperationException("Could not determine the current user's SID for the inspector pipe DACL.");
        security.AddAccessRule(new PipeAccessRule(user, PipeAccessRights.ReadWrite | PipeAccessRights.CreateNewInstance, AccessControlType.Allow));
        return security;
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

/// <summary>
/// Asserts a connected Unix-domain-socket peer is the same user as this process (defense-in-depth over the
/// 0700 directory).
///
/// - SO_PEERCRED on Linux, getpeereid on macOS.
/// - Fails open only when the credential can't be read (unknown platform / syscall error) — the directory
///   mode is the baseline there — but fails closed on a confirmed different uid.
/// </summary>
internal static class UnixPeerCredentials
{
    // Linux SOL_SOCKET / SO_PEERCRED. ucred is { int pid; uint uid; uint gid; } — 12 bytes; uid at offset 4.
    private const int SOL_SOCKET_LINUX = 1;
    private const int SO_PEERCRED_LINUX = 17;

    [DllImport("libc", SetLastError = true)]
    private static extern uint geteuid();

    [DllImport("libc", SetLastError = true, EntryPoint = "getsockopt")]
    private static extern int getsockopt(int sockfd, int level, int optname, byte[] optval, ref int optlen);

    [DllImport("libc", SetLastError = true, EntryPoint = "getpeereid")]
    private static extern int getpeereid(int sockfd, out uint euid, out uint egid);

    public static bool IsSameUser(Socket socket)
    {
        try
        {
            var fd = (int)socket.Handle;
            var self = geteuid();

            if (OperatingSystem.IsLinux())
            {
                var ucred = new byte[12];
                var length = ucred.Length;
                if (getsockopt(fd, SOL_SOCKET_LINUX, SO_PEERCRED_LINUX, ucred, ref length) != 0)
                {
                    return true; // couldn't read the credential — rely on the 0700 directory
                }

                var peerUid = BitConverter.ToUInt32(ucred, 4);
                return peerUid == self;
            }

            if (OperatingSystem.IsMacOS())
            {
                if (getpeereid(fd, out var euid, out _) != 0)
                {
                    return true; // couldn't read the credential — rely on the 0700 directory
                }

                return euid == self;
            }

            return true; // unknown Unix — the directory mode is the protection
        }
        catch
        {
            return true; // P/Invoke unavailable — don't block a legitimate same-user connection
        }
    }
}
