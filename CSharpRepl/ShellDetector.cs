// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace CSharpRepl;

/// <summary>
/// Best-effort detection of the interactive shell that launched this process, used by
/// <c>csharprepl inspect init</c> to emit env-var exports in the right syntax without the user
/// having to pass <c>--shell</c>. The reliable signal is the parent process: shell variables like
/// <c>BASH_VERSION</c>/<c>ZSH_VERSION</c> aren't exported (a child can't see them) and <c>SHELL</c>
/// is the login shell, not necessarily the one currently driving the terminal. So we walk up the
/// process tree and match the first recognized shell name.
/// </summary>
internal static class ShellDetector
{
    /// <summary>
    /// Returns the normalized shell name (<c>"pwsh"</c>, <c>"cmd"</c>, <c>"bash"</c>, or <c>"fish"</c>)
    /// of the nearest ancestor process that is a recognized shell, or <c>null</c> when none is found
    /// (the caller should fall back to an OS default). Never throws.
    /// </summary>
    public static string? DetectShell()
    {
        try
        {
            // Walk up a few levels so intermediate hosts (the apphost, `dotnet`, `dotnet tool run`)
            // between us and the real shell don't hide it. The cap also guards against pid cycles.
            var pid = Environment.ProcessId;
            for (var depth = 0; depth < 5; depth++)
            {
                var ppid = GetParentPid(pid);
                if (ppid <= 0)
                {
                    break;
                }

                string name;
                try
                {
                    using var parent = Process.GetProcessById(ppid);
                    name = parent.ProcessName;
                }
                catch
                {
                    // Parent already exited, or the pid was reused for something we can't open.
                    break;
                }

                var shell = MapShellName(name);
                if (shell is not null)
                {
                    return shell;
                }

                pid = ppid;
            }
        }
        catch
        {
            // Best-effort only: any failure falls back to the caller's OS default.
        }

        return null;
    }

    /// <summary>
    /// Maps a raw process name (no path, no <c>.exe</c>, any casing) to the normalized shell name
    /// understood by the export builder, or <c>null</c> if it isn't a recognized shell.
    /// </summary>
    internal static string? MapShellName(string processName) => processName.ToLowerInvariant() switch
    {
        "pwsh" or "powershell" => "pwsh",
        "cmd" => "cmd",
        "bash" or "sh" or "zsh" or "dash" or "ash" or "ksh" => "bash",
        "fish" => "fish",
        _ => null,
    };

    /// <summary>
    /// Returns the parent process id of <paramref name="pid"/>, or a non-positive value when it can't
    /// be determined. Windows and Linux resolve any pid; macOS resolves only the current process's
    /// parent (the common single-hop case), since deeper walks there would require fragile struct
    /// offsets into <c>kinfo_proc</c>.
    /// </summary>
    private static int GetParentPid(int pid)
    {
        if (OperatingSystem.IsWindows())
        {
            return GetParentPidWindows(pid);
        }
        if (OperatingSystem.IsLinux())
        {
            return GetParentPidLinux(pid);
        }
        if (OperatingSystem.IsMacOS() && pid == Environment.ProcessId)
        {
            return getppid();
        }
        return -1;
    }

    private static int GetParentPidLinux(int pid)
    {
        // /proc/<pid>/stat is "<pid> (<comm>) <state> <ppid> ...". comm can contain spaces and
        // parentheses, so anchor parsing after the final ')' rather than splitting from the start.
        string stat;
        try
        {
            stat = File.ReadAllText($"/proc/{pid}/stat");
        }
        catch
        {
            return -1;
        }

        var rparen = stat.LastIndexOf(')');
        if (rparen < 0 || rparen + 2 >= stat.Length)
        {
            return -1;
        }

        // After ") " come: state ppid ...
        var fields = stat[(rparen + 2)..].Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return fields.Length >= 2 && int.TryParse(fields[1], out var ppid) ? ppid : -1;
    }

    private static int GetParentPidWindows(int pid)
    {
        // PROCESS_QUERY_LIMITED_INFORMATION is grantable across integrity levels and is enough for
        // ProcessBasicInformation, which carries the parent pid in InheritedFromUniqueProcessId.
        var handle = OpenProcess(ProcessQueryLimitedInformation, false, pid);
        if (handle == IntPtr.Zero)
        {
            return -1;
        }

        try
        {
            var info = default(ProcessBasicInformation);
            var status = NtQueryInformationProcess(handle, ProcessBasicInformationClass, ref info, Marshal.SizeOf(info), out _);
            return status == 0 ? (int)info.InheritedFromUniqueProcessId.ToUInt64() : -1;
        }
        finally
        {
            CloseHandle(handle);
        }
    }

    [DllImport("libc", EntryPoint = "getppid")]
    private static extern int getppid();

    private const int ProcessQueryLimitedInformation = 0x1000;
    private const int ProcessBasicInformationClass = 0;

    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessBasicInformation
    {
        public IntPtr ExitStatus;
        public IntPtr PebBaseAddress;
        public IntPtr AffinityMask;
        public IntPtr BasePriority;
        public UIntPtr UniqueProcessId;
        public UIntPtr InheritedFromUniqueProcessId;
    }

    [DllImport("ntdll.dll")]
    private static extern int NtQueryInformationProcess(IntPtr processHandle, int processInformationClass, ref ProcessBasicInformation processInformation, int processInformationLength, out int returnLength);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(int desiredAccess, bool inheritHandle, int processId);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr handle);
}
