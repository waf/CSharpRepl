// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using CSharpRepl.Services.Logging;
using System;
using System.IO;
using System.IO.Abstractions;
using System.Linq;
using System.Runtime.InteropServices;

namespace CSharpRepl.Services.Roslyn.References;

/// <summary>
/// Determines locations of Reference Assemblies and Implementation Assemblies.
/// We need the Reference Assemblies for the Workspace API, and Implementation Assemblies for the CSharpScript APIs.
/// Sets of assemblies are per-shared-framework, e.g. Microsoft.NETCore.App or Microsoft.AspNetCore.App.
/// </summary>
/// <remarks>https://github.com/dotnet/designs/blob/main/accepted/2019/targeting-packs-and-runtime-packs.md</remarks>
internal sealed class DotNetInstallationLocator
{
    private readonly ITraceLogger logger;
    private readonly IFileSystem io;
    private readonly string dotnetRuntimePath;
    private readonly string userProfilePath;

    // used at runtime
    public DotNetInstallationLocator(ITraceLogger logger) : this(
       logger: logger,
       io: new FileSystem(),
       // a path like C:\Program Files\dotnet
       dotnetRuntimePath: Path.GetFullPath(Path.Combine(RuntimeEnvironment.GetRuntimeDirectory(), "../../../")),
       // a path like C:\Users\username
       userProfilePath: Path.GetFullPath(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile))
    )
    { }

    // used for unit testing, it will inject fake IO
    internal DotNetInstallationLocator(ITraceLogger logger, IFileSystem io, string dotnetRuntimePath, string userProfilePath)
    {
        this.logger = logger;
        this.io = io;
        this.dotnetRuntimePath = dotnetRuntimePath;
        this.userProfilePath = userProfilePath;
    }

    /// <summary>
    /// Finds the path to the specified framework's reference assemblies and implementation assemblies.
    /// Usually, these are somewhere under the global dotnet installation, but they can also be in ~/.nuget.
    /// </summary>
    /// <param name="framework">A shared framework, like Microsoft.NETCore.App</param>
    /// <param name="version">The desired shared framework version, like 5.0.4</param>
    public (string referencePath, string implementationPath) FindInstallation(string framework, Version version)
    {
        // first, try loading from the system-wide folders, e.g. in C:\Program Files\dotnet\
        var referenceAssemblyRoot = Path.Combine(dotnetRuntimePath, "packs", framework + ".Ref");
        var implementationAssemblyRoot = Path.Combine(dotnetRuntimePath, "shared", framework);

        logger.LogPaths("Available Reference Assemblies", () => ListDirectoriesIfExists(referenceAssemblyRoot));
        logger.LogPaths("Available Implementation Assemblies", () => ListDirectoriesIfExists(implementationAssemblyRoot));

        var referencePath = GetGlobalReferenceAssemblyPath(referenceAssemblyRoot, version);
        var implementationPath = GetGlobalImplementationAssemblyPath(implementationAssemblyRoot, version);

        // second, try loading from installed nuget packages, e.g. ~\.nuget\packages\microsoft.netcore.app.*
        if (referencePath is null)
        {
            referencePath = FallbackToNugetReferencePath(framework, version);
        }
        if (implementationPath is null)
        {
            implementationPath = FallbackToNugetImplementationPath(framework, version);
        }

        if (referencePath is null || implementationPath is null)
        {
            throw new InvalidOperationException(
                "Could not determine the .NET SDK to use. Please install the latest .NET SDK installer from https://dotnet.microsoft.com/download" + Environment.NewLine
                + $@"Tried to find {version} with reference assemblies in ""{referenceAssemblyRoot}"" and implementation assemblies in ""{implementationAssemblyRoot}""." + Environment.NewLine
                + $@"Also tried falling back to ""{referencePath}"" and ""{implementationPath}"""
            );
        }

        return (referencePath, implementationPath);
    }

    /// <summary>
    /// Returns path to globally installed Reference Assemblies like C:\Program Files\dotnet\packs\Microsoft.NETCore.App.Ref\5.0.0\ref\net5.0
    /// </summary>
    private string? GetGlobalReferenceAssemblyPath(string referenceAssemblyRoot, Version version)
    {
        if (!io.Directory.Exists(referenceAssemblyRoot)) return null;

        var referenceAssemblyPath = io.Directory
            .GetDirectories(referenceAssemblyRoot, "net*" + version.Major + "." + version.Minor + "*", SearchOption.AllDirectories)
            .OrderBy(path => path)
            .LastOrDefault();

        if (referenceAssemblyPath is null) return null;

        return Path.GetFullPath(referenceAssemblyPath);
    }

    /// <summary>
    /// Returns the path to globally installed Implementation Assemblies like C:\Program Files\dotnet\shared\Microsoft.NETCore.App\5.0.10
    /// </summary>
    private string? GetGlobalImplementationAssemblyPath(string implementationAssemblyRoot, Version version)
    {
        if (!io.Directory.Exists(implementationAssemblyRoot)) return null;

        var configuredFrameworkAndVersion = io.Directory
            .GetDirectories(implementationAssemblyRoot, version.Major + "." + version.Minor + "*")
            .OrderBy(path => SharedFramework.ToDotNetVersion(Path.GetFileName(path)))
                .ThenBy(path => path + ".") // trick to get e.g. 6.0 to come after 6.0-preview
            .LastOrDefault();

        return configuredFrameworkAndVersion;
    }

    /// <summary>
    /// Returns a path like C:\Users\username\.nuget\packages\microsoft.aspnetcore.app.ref\5.0.0\ref\net5.0
    /// or equivalent on mac os / linux.
    /// </summary>
    private string? FallbackToNugetReferencePath(string framework, Version version)
    {
        string nugetReferenceAssemblyRoot = Path.Combine(
            userProfilePath,
            ".nuget",
            "packages",
            framework.ToLowerInvariant() + ".ref"
        );

        logger.LogPaths("NuGet Reference Assemblies", () => ListDirectoriesIfExists(nugetReferenceAssemblyRoot));

        return GetGlobalReferenceAssemblyPath(nugetReferenceAssemblyRoot, version);
    }

    /// <summary>
    /// Returns a path like C:\Users\username\.nuget\packages\microsoft.aspnetcore.app.runtime.win-x86\5.0.8\runtimes\win-x86\lib\net5.0
    /// or equivalent on mac os / linux.
    /// </summary>
    private string? FallbackToNugetImplementationPath(string framework, Version version)
    {
        string? platform = OperatingSystem.IsWindows() ? "win"
            : OperatingSystem.IsLinux() ? "linux"
            : OperatingSystem.IsMacOS() ? "osx"
            : null;

        if (platform is null) return null;

        var nugetImplementationAssemblyRoot = Path.Combine(
            userProfilePath,
            ".nuget",
            "packages",
            $"{framework}.runtime.{platform}-{RuntimeInformation.ProcessArchitecture}".ToLowerInvariant()
        );

        logger.LogPaths("NuGet Implementation Assemblies", () => ListDirectoriesIfExists(nugetImplementationAssemblyRoot));

        var implementationPath = GetGlobalImplementationAssemblyPath(nugetImplementationAssemblyRoot, version);

        if (implementationPath is null) return null;

        return io.Directory
            .GetDirectories(implementationPath, "net*", SearchOption.AllDirectories)
            .LastOrDefault();
    }

    private string[] ListDirectoriesIfExists(string referenceAssemblyRoot) =>
        io.Directory.Exists(referenceAssemblyRoot)
        ? io.Directory.GetDirectories(referenceAssemblyRoot)
        : Array.Empty<string>();
}
