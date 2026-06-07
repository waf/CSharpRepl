// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Diagnostics;
using System.IO;
using Xunit;

namespace CSharpRepl.Tests;

/// <summary>
/// Shared helpers for the inspector integration tests: locate the built bootstrap + test target, and launch
/// the unmodified target as a separate process with the inspector injected via DOTNET_STARTUP_HOOKS. The
/// target knows nothing about the inspector; the hook brings it up before Main.
/// </summary>
internal static class InspectorTestSupport
{
    public static Process StartHookedTarget()
    {
        var startInfo = new ProcessStartInfo(ResolveTestTarget())
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        startInfo.Environment["DOTNET_STARTUP_HOOKS"] = ResolveInspectorBootstrap();
        startInfo.Environment["NO_COLOR"] = "1";

        return Process.Start(startInfo)
            ?? throw new InvalidOperationException("Failed to start the inspector test target process.");
    }

    public static string ResolveInspectorBootstrap()
    {
        var dir = AppContext.BaseDirectory.Replace(
            $"CSharpRepl.Tests{Path.DirectorySeparatorChar}bin",
            $"CSharpRepl.Inspector{Path.DirectorySeparatorChar}bin");
        var bootstrap = Path.Combine(dir, "CSharpRepl.Inspector.dll");
        Assert.True(File.Exists(bootstrap), $"Could not find the built inspector bootstrap at '{bootstrap}'.");
        return bootstrap;
    }

    public static string ResolveTestTarget()
    {
        var dir = AppContext.BaseDirectory.Replace(
            $"CSharpRepl.Tests{Path.DirectorySeparatorChar}bin",
            $"CSharpRepl.Inspector.TestTarget{Path.DirectorySeparatorChar}bin");
        var executable = Path.Combine(dir, OperatingSystem.IsWindows()
            ? "CSharpRepl.Inspector.TestTarget.exe"
            : "CSharpRepl.Inspector.TestTarget");
        Assert.True(File.Exists(executable), $"Could not find the built inspector test target at '{executable}'.");
        return executable;
    }
}
