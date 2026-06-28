// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace CSharpRepl.Commands;

/// <summary>
/// Best-effort detection of the interactive shell that launched us, so `csharprepl connect init`
/// can emit env-var exports in the right syntax without the user passing `--shell`. We match the
/// parent process, not env vars, because:
/// - shell vars like `BASH_VERSION`/`ZSH_VERSION` aren't exported to children
/// - `SHELL` is the login shell, not necessarily the one driving the terminal
/// </summary>
internal static class ShellDetector
{
    /// <summary>
    /// Returns the normalized shell name (`pwsh`, `cmd`, `bash`, or `fish`) of the nearest ancestor
    /// process that is a recognized shell, or `null` when none is found (the caller should fall back
    /// to an OS default). Never throws.
    /// </summary>
    public static string? DetectShell()
    {
        try
        {
            return ResolveShellFromAncestry(GetAncestorProcessNames());
        }
        catch
        {
            // Best-effort only: any failure falls back to the caller's OS default.
            return null;
        }
    }

    /// <summary>
    /// Collects up to a few ancestor process names, nearest first. Best-effort:
    /// - walks several levels so hosts between us and the shell (apphost, `dotnet`, a transient `cmd /c` shim) don't hide it
    /// - the depth cap also guards against pid cycles
    /// - stops at the first ancestor it can't resolve (exited / pid reused / unsupported platform)
    /// </summary>
    private static List<string> GetAncestorProcessNames()
    {
        var names = new List<string>();
        var pid = Environment.ProcessId;
        for (var depth = 0; depth < 5; depth++)
        {
            var ppid = GetParentPid(pid);
            if (ppid <= 0)
            {
                break;
            }

            try
            {
                using var parent = Process.GetProcessById(ppid);
                names.Add(parent.ProcessName);
            }
            catch
            {
                // Parent already exited, or the pid was reused for something we can't open.
                break;
            }

            pid = ppid;
        }

        return names;
    }

    /// <summary>
    /// Picks the shell from an ancestry chain (nearest first): the first recognized shell wins, except a
    /// `cmd` whose own parent is also a recognized shell. That's the transient `cmd.exe /c` wrapper around
    /// our `.cmd` tool shim that Windows inserts when a non-cmd shell runs it (RID-specific tools ship a
    /// `.cmd`, not an apphost `.exe`):
    /// - shim wrapper — its parent is the real shell, so skip it and keep walking
    /// - genuine interactive cmd — its parent is the terminal/conhost (not a shell), so keep it
    /// Returns `null` when no ancestor is a recognized shell.
    /// </summary>
    internal static string? ResolveShellFromAncestry(IReadOnlyList<string> ancestorNames)
    {
        for (var i = 0; i < ancestorNames.Count; i++)
        {
            var shell = MapShellName(ancestorNames[i]);
            if (shell is null)
            {
                continue;
            }

            // Transient `cmd /c <shim>.cmd` wrapper: its parent is the real shell, so keep walking.
            if (shell == "cmd" && i + 1 < ancestorNames.Count && MapShellName(ancestorNames[i + 1]) is not null)
            {
                continue;
            }

            return shell;
        }

        return null;
    }

    /// <summary>
    /// Maps a raw process name (no path, no `.exe`, any casing) to the normalized shell name
    /// understood by the export builder, or `null` if it isn't a recognized shell.
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
    /// Returns the parent pid of <paramref name="pid"/>, or a non-positive value when it can't be
    /// determined. Per platform:
    /// - Windows / Linux — resolve any pid
    /// - macOS — only the current process's parent (the common single-hop case); deeper walks would need fragile `kinfo_proc` struct offsets
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
