// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.IO;
using System.Linq;
using System.Text;
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
    private readonly StringBuilder stdout;

    public NugetPackageInstallerTests()
    {
        var (console, stdout) = FakeConsole.CreateStubbedOutput();
        this.stdout = stdout;
        installer = new NugetPackageInstaller(console, new Configuration());
    }

    public ValueTask InitializeAsync() => ValueTask.CompletedTask;
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    /// <summary>
    /// https://github.com/waf/CSharpRepl/issues/58
    /// </summary>
    [Fact]
    public async Task InstallRuntimeSpecificPackage()
    {
        var references = await installer.InstallAsync("System.Management", "6.0.0", TestContext.Current.CancellationToken);

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
        // Microsoft.CSharp's nearest target is an empty '_._' placeholder because it's provided by the shared
        // framework on modern .NET. A correct restore therefore contributes no package references of its own, and
        // must still report success rather than treating "no assets" as a failed install.
        var references = await installer.InstallAsync("Microsoft.CSharp", "4.7.0", TestContext.Current.CancellationToken);

        Assert.Empty(references);
        var output = stdout.ToString().RemoveFormatting();
        Assert.Contains("Adding references for 'Microsoft.CSharp", output);
        Assert.DoesNotContain("Could not restore", output);
    }

    [Fact]
    public async Task InstallPackageThatOnlyContainsDependencies()
    {
        // humanizer does not target any frameworks itself, but depends on nuget packages that do.
        var references = await installer.InstallAsync("Humanizer", "2.14.1", TestContext.Current.CancellationToken);

        Assert.True(references.Length >= 1);
        var reference = references.FirstOrDefault(r => r.FilePath.EndsWith("Humanizer.dll", StringComparison.OrdinalIgnoreCase));
        Assert.NotNull(reference);
        Assert.True(reference.FilePath.Contains("lib", StringComparison.OrdinalIgnoreCase));
    }


    [Fact] // https://github.com/waf/CSharpRepl/issues/251
    public async Task InstallPackageThatContainsImplicitVersioning()
    {
        // The package version is "1.2" but if we accidentally normalize the version, it normalizes to 1.2.0 and causes directory not found errors
        var references = await installer.InstallAsync("Emik.Results", "1.2", TestContext.Current.CancellationToken);

        Assert.True(references.Length >= 1);
        var reference = references.FirstOrDefault(r => r.FilePath.EndsWith("Emik.Results.dll", StringComparison.OrdinalIgnoreCase));
        Assert.NotNull(reference);
        Assert.True(reference.FilePath.Contains("lib", StringComparison.OrdinalIgnoreCase));
    }

    [Fact] // https://github.com/waf/CSharpRepl/issues/392
    public async Task InstallMetapackageWithFrameworkSpecificDependencies()
    {
        // Microsoft.Data.Sqlite has no lib dll of its own (only lib/netstandard2.0/_._); all of its
        // assemblies come from dependencies that are declared under a framework-specific (.NETStandard2.0)
        // dependency group. Unlike Humanizer (whose dependencies live in an "Any" group), this exercises
        // matching the dependency group against the target framework independently of the (empty) lib folder.
        var references = await installer.InstallAsync("Microsoft.Data.Sqlite", "9.0.1", TestContext.Current.CancellationToken);

        Assert.True(references.Length >= 1);
        var reference = references.FirstOrDefault(r => r.FilePath.EndsWith("Microsoft.Data.Sqlite.dll", StringComparison.OrdinalIgnoreCase));
        Assert.NotNull(reference);
        Assert.True(reference.FilePath.Contains("lib", StringComparison.OrdinalIgnoreCase));
    }
}
