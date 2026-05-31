// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Xunit;

namespace CSharpRepl.Tests;

/// <summary>
/// Black-box tests that launch the built csharprepl executable as a separate process with truly
/// redirected stdin/stdout/stderr. In-process stubs might not be guaranteed to behave the same
/// as actually redirected input.
/// </summary>
public class CSharpReplProcessTests
{
    [Fact]
    public async Task Eval_AutoPrintsResult_OverRealRedirectedPipe()
    {
        var (exitCode, stdout, stderr) = await RunAsync("-e", "1 + 1");

        Assert.True(exitCode == 0, $"Expected success but got {exitCode}. stderr: {stderr}");
        Assert.Contains("2", stdout);
    }

    [Fact]
    public async Task Eval_WithNugetReference_WorksWhenOutputRedirected()
    {
        // Regression test for "The handle is invalid": with stdout redirected, NuGet restore must not
        // touch TTY-only console APIs (cursor movement, buffer width).
        var (exitCode, stdout, stderr) = await RunAsync(
            "-e", "Newtonsoft.Json.JsonConvert.SerializeObject(new[]{1,2,3})",
            "-r", "nuget: Newtonsoft.Json");

        Assert.True(exitCode == 0, $"Expected success but got {exitCode}. stderr: {stderr}");
        Assert.Contains("[1,2,3]", stdout);
        Assert.DoesNotContain("handle is invalid", stdout + stderr, StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<(int exitCode, string stdout, string stderr)> RunAsync(params string[] args)
    {
        var startInfo = new ProcessStartInfo(ResolveExecutable())
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var arg in args) startInfo.ArgumentList.Add(arg);
        startInfo.Environment["NO_COLOR"] = "1"; // keep output free of ANSI so assertions are simple

        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Failed to start csharprepl process.");
        process.StandardInput.Close(); // not piping stdin; close it so any stdin read returns immediately

        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        return (process.ExitCode, await stdoutTask, await stderrTask);
    }

    /// <summary>
    /// Resolves the csharprepl executable built by the referenced CSharpRepl project.
    /// </summary>
    private static string ResolveExecutable()
    {
        var exeDir = AppContext.BaseDirectory.Replace(
            $"CSharpRepl.Tests{Path.DirectorySeparatorChar}bin",
            $"CSharpRepl{Path.DirectorySeparatorChar}bin");
        var exe = Path.Combine(exeDir, OperatingSystem.IsWindows() ? "CSharpRepl.exe" : "CSharpRepl");

        Assert.True(File.Exists(exe), $"Could not find built csharprepl executable at '{exe}'. Build the CSharpRepl project first.");
        return exe;
    }
}
