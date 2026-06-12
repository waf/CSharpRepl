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

    [Fact]
    public async Task Eval_WithProjectReference_DoesNotClobberCoreLibrary()
    {
        // Regression test for https://github.com/waf/CSharpRepl/issues/399. Referencing a project that
        // targets a modern .NET pulls in the framework *reference* assemblies (the Microsoft.NETCore.App.Ref
        // targeting pack, whose System.Runtime.dll/mscorlib.dll define System.Object), which conflict with
        // the framework *implementation* assemblies the REPL is already configured with. Causes the error
        // CS0518: Predefined type 'System.Object' is not defined or imported".
        //
        // The normal REPL bypasses this with a warm-up, but `-r foo.csproj` evaluates the reference first.
        // It must be reproduced via a separate process (the test host already loads the runtime).
        //
        // The project must target the running .NET (a netstandard project's facade ref pack does not define
        // System.Object and so would not reproduce the bug); generate a throwaway one so the test adapts to
        // whatever runtime the tests run on.
        var tfm = $"net{Environment.Version.Major}.0";
        var dir = Path.Combine(Path.GetTempPath(), "csharprepl-issue399-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var project = Path.Combine(dir, "RefProj.csproj");
            await File.WriteAllTextAsync(project, $"""
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <TargetFramework>{tfm}</TargetFramework>
                  </PropertyGroup>
                </Project>
                """, TestContext.Current.CancellationToken);

            var (exitCode, stdout, stderr) = await RunAsync("-e", "1 + 1", "-r", project);

            Assert.True(exitCode == 0, $"Expected success but got {exitCode}. stdout: {stdout} stderr: {stderr}");
            Assert.Contains("2", stdout);
            Assert.DoesNotContain("CS0518", stdout + stderr);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
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
            Path.Combine("Tests", "CSharpRepl.Tests", "bin"),
            Path.Combine("CSharpRepl", "bin"));
        var exe = Path.Combine(exeDir, OperatingSystem.IsWindows() ? "CSharpRepl.exe" : "CSharpRepl");

        Assert.True(File.Exists(exe), $"Could not find built csharprepl executable at '{exe}'. Build the CSharpRepl project first.");
        return exe;
    }
}
