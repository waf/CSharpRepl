// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CSharpRepl.Services;
using CSharpRepl.Services.Nuget;
using CSharpRepl.Services.Roslyn;
using Microsoft.CodeAnalysis;
using Xunit;

namespace CSharpRepl.Tests;

[Collection(nameof(RoslynServices))]
public class NugetPackageInstallerTests : IAsyncLifetime
{
    private readonly NugetPackageInstaller installer;

    public NugetPackageInstallerTests()
    {
        installer = new NugetPackageInstaller(FakeConsole.CreateStubbedOutput().console, new Configuration());
    }

    public Task InitializeAsync() => Task.CompletedTask;
    public Task DisposeAsync() => Task.CompletedTask;

    /// <summary>
    /// https://github.com/waf/CSharpRepl/issues/58
    /// </summary>
    [Fact]
    public async Task InstallRuntimeSpecificPackage()
    {
        var references = await installer.InstallAsync("System.Management", "6.0.0");

        Assert.True(references.Length >= 1);
        var reference = references.FirstOrDefault(r => r.FilePath.EndsWith("System.Management.dll", StringComparison.OrdinalIgnoreCase));
        Assert.NotNull(reference);

        var winRuntimeSelected = reference.FilePath.Contains(Path.Combine("runtimes", "win", "lib"), StringComparison.OrdinalIgnoreCase);
        var isWin = Environment.OSVersion.Platform == PlatformID.Win32NT;
        Assert.Equal(isWin, winRuntimeSelected);
    }

    [Fact]
    public async Task InstallPackageWithSupportedButEmptyTargets()
    {
        var references = await installer.InstallAsync("Microsoft.CSharp", "4.7.0"); //some targets contains only empty file '_._'

        Assert.True(references.Length >= 1);
        var reference = references.FirstOrDefault(r => r.FilePath.EndsWith("Microsoft.CSharp.dll", StringComparison.OrdinalIgnoreCase));
        Assert.NotNull(reference);
        Assert.True(reference.FilePath.Contains("lib", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task InstallPackageThatOnlyContainsDependencies()
    {
        // humanizer does not target any frameworks itself, but depends on nuget packages that do.
        var references = await installer.InstallAsync("Humanizer", "2.14.1");

        Assert.True(references.Length >= 1);
        var reference = references.FirstOrDefault(r => r.FilePath.EndsWith("Humanizer.dll", StringComparison.OrdinalIgnoreCase));
        Assert.NotNull(reference);
        Assert.True(reference.FilePath.Contains("lib", StringComparison.OrdinalIgnoreCase));
    }
}
