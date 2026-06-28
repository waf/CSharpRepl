// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Diagnostics;
using System.IO;
using CSharpRepl.Services.Dotnet;
using Xunit;

namespace CSharpRepl.Tests;

/// <summary>
/// Shared helpers for the connector integration tests: locate the built bootstrap, build the test-target
/// fixture, and launch the unmodified target as a separate process with the connector injected via
/// DOTNET_STARTUP_HOOKS. The target knows nothing about the connector; the hook brings it up before Main.
/// </summary>
internal static class ConnectorTestSupport
{
    private const string TestTargetName = "CSharpRepl.InjectedHook.TestTarget";

    // The test target is a fixture project under Data\ (copied next to the test via Content), built on demand
    // like the solution fixtures. Built once per test process and shared across the connector test classes.
    private static readonly Lazy<string> builtTestTarget = new(BuildTestTarget);

    public static Process StartHookedTarget()
    {
        var startInfo = new ProcessStartInfo(ResolveTestTarget())
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        startInfo.Environment["DOTNET_STARTUP_HOOKS"] = ResolveConnectorBootstrap();
        startInfo.Environment["NO_COLOR"] = "1";

        return Process.Start(startInfo)
            ?? throw new InvalidOperationException("Failed to start the connector test target process.");
    }

    public static string ResolveConnectorBootstrap()
    {
        var dir = AppContext.BaseDirectory.Replace(
            Path.Combine("Tests", "CSharpRepl.Tests", "bin"),
            Path.Combine("InjectedHook", "CSharpRepl.InjectedHook", "bin"));
        var bootstrap = Path.Combine(dir, "CSharpRepl.InjectedHook.dll");
        Assert.True(File.Exists(bootstrap), $"Could not find the built connector bootstrap at '{bootstrap}'.");
        return bootstrap;
    }

    public static string ResolveTestTarget() => builtTestTarget.Value;

    private static string BuildTestTarget()
    {
        // The fixture is copied next to the test by Content Include="Data\**"; build it in place.
        var projectDirectory = Path.Combine(AppContext.BaseDirectory, "Data", TestTargetName);
        Assert.True(Directory.Exists(projectDirectory),
            $"Connector test target fixture not found at '{projectDirectory}'. Is it copied via Content Include=\"Data\\**\"?");

        var (console, _) = FakeConsole.CreateStubbedOutput();
        var (exitCode, output) = new DotnetBuilder(console).Build(projectDirectory);
        Assert.True(exitCode == 0,
            $"Building the connector test target failed (exit {exitCode}):{Environment.NewLine}{string.Join(Environment.NewLine, output)}");

        // DotnetBuilder doesn't pass a configuration, so it always produces the Debug apphost (matching the
        // other on-demand-built fixtures, e.g. DemoProject3).
        var executable = Path.Combine(projectDirectory, "bin", "Debug", "net10.0",
            OperatingSystem.IsWindows() ? TestTargetName + ".exe" : TestTargetName);
        Assert.True(File.Exists(executable), $"Built connector test target not found at '{executable}'.");
        return executable;
    }
}
