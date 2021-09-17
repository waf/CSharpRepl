// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using CSharpRepl.Services.Extensions;
using CSharpRepl.Services.Logging;
using Microsoft.CodeAnalysis;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;

namespace CSharpRepl.Services.Roslyn.References
{
    /// <summary>
    /// Determines locations of Reference Assemblies and Implementation Assemblies.
    /// We need the Reference Assemblies for the Workspace API, and Implementation Assemblies for the CSharpScript APIs.
    /// Sets of assemblies are per-shared-framework, e.g. Microsoft.NETCore.App or Microsoft.AspNetCore.App.
    /// </summary>
    /// <remarks>https://github.com/dotnet/designs/blob/main/accepted/2019/targeting-packs-and-runtime-packs.md</remarks>
    internal sealed class DotNetInstallationLocator
    {
        private readonly ITraceLogger logger;

        public DotNetInstallationLocator(ITraceLogger logger)
        {
            this.logger = logger;
        }

        /// <summary>
        /// Returns a SharedFramework that contains a list of reference and implementation assemblies in that framework.
        /// It first tries to use a globally installed shared framework (e.g. in C:\Program Files\dotnet\) and falls back
        /// to a shared framework installed in C:\Users\username\.nuget\packages\.
        ///
        /// Microsoft.NETCore.App is always returned, in addition to any other specified framework.
        /// </summary>
        /// <exception cref="InvalidOperationException">If no shared framework could be found</exception>
        public SharedFramework[] GetSharedFrameworkConfiguration(string framework, Version version)
        {
            var dotnetRoot = GetDotNetRootPath();

            // first, try loading from the system-wide folders, e.g. in C:\Program Files\dotnet\
            var referenceAssemblyRoot = Path.Combine(dotnetRoot, "packs", framework + ".Ref");
            var implementationAssemblyRoot = Path.Combine(dotnetRoot, "shared", framework);

            logger.LogPaths("Available Reference Assemblies", () => Directory.GetDirectories(referenceAssemblyRoot));
            logger.LogPaths("Available Implementation Assemblies", () => Directory.GetDirectories(implementationAssemblyRoot));

            string? referencePath = GetGlobalReferenceAssemblyPath(referenceAssemblyRoot, version);
            string? implementationPath = GetGlobalImplementationAssemblyPath(implementationAssemblyRoot, version);

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

            var referenceDlls = CreateDefaultReferences(
                referencePath,
                Directory.GetFiles(referencePath, "*.dll", SearchOption.TopDirectoryOnly)
            );
            var implementationDlls = CreateDefaultReferences(
                implementationPath,
                Directory.GetFiles(implementationPath, "*.dll", SearchOption.TopDirectoryOnly)
            );

            // Microsoft.NETCore.App is always loaded.
            // e.g. if we're loading Microsoft.AspNetCore.App, load it alongside Microsoft.NETCore.App.
            return framework switch
            {
                SharedFramework.NetCoreApp => new[] {
                    new SharedFramework(referencePath, referenceDlls, implementationPath, implementationDlls)
                },
                _ => GetSharedFrameworkConfiguration(SharedFramework.NetCoreApp, version)
                    .Append(new SharedFramework(referencePath, referenceDlls, implementationPath, implementationDlls))
                    .ToArray()
            };
        }

        /// <summary>
        /// Returns path to globally installed Reference Assemblies like C:\Program Files\dotnet\packs\Microsoft.NETCore.App.Ref\5.0.0\ref\net5.0
        /// </summary>
        private static string? GetGlobalReferenceAssemblyPath(string referenceAssemblyRoot, Version version)
        {
            var referenceAssemblyPath = Directory
                .GetDirectories(referenceAssemblyRoot, "net*" + version.Major + "." + version.Minor + "*", SearchOption.AllDirectories)
                .LastOrDefault();

            if (referenceAssemblyPath is null)
            {
                return null;
            }

            return Path.GetFullPath(referenceAssemblyPath);
        }

        /// <summary>
        /// Returns the path to globally installed Implementation Assemblies like C:\Program Files\dotnet\shared\Microsoft.NETCore.App\5.0.10
        /// </summary>
        private static string? GetGlobalImplementationAssemblyPath(string implementationAssemblyRoot, Version version)
        {
            var configuredFrameworkAndVersion = Directory
                .GetDirectories(implementationAssemblyRoot, version.Major + "." + version.Minor + "*")
                .OrderBy(path => ParseDotNetVersion(path))
                .LastOrDefault();

            return configuredFrameworkAndVersion;

            static Version ParseDotNetVersion(string path)
            {
                var versionString = Path.GetFileName(path).Split('-', 2).First(); // discard trailing preview versions, e.g. 6.0.0-preview.4.21253.7 
                return new Version(versionString);
            }
        }

        /// <summary>
        /// Returns a path like C:\Users\username\.nuget\packages\microsoft.aspnetcore.app.ref\5.0.0\ref\net5.0
        /// or equivalent on mac os / linux.
        /// </summary>
        private string? FallbackToNugetReferencePath(string framework, Version version)
        {
            string nugetReferenceAssemblyRoot = Path.Combine(
                Path.GetFullPath(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)),
                @".nuget\packages\",
                framework.ToLowerInvariant() + ".ref"
            );

            logger.LogPaths("NuGet Reference Assemblies", () => Directory.GetDirectories(nugetReferenceAssemblyRoot));

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
                Path.GetFullPath(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)),
                @".nuget\packages\",
                (framework + ".runtime." + platform + "-" + RuntimeInformation.ProcessArchitecture).ToLowerInvariant()
            );

            logger.LogPaths("NuGet Implementation Assemblies", () => Directory.GetDirectories(nugetImplementationAssemblyRoot));

            var implementationPath = GetGlobalImplementationAssemblyPath(nugetImplementationAssemblyRoot, version);

            if (implementationPath is null) return null;

            return Directory
                .GetDirectories(implementationPath, "net*", SearchOption.AllDirectories)
                .LastOrDefault();
        }

        private static string GetDotNetRootPath()
        {
            var dotnetRuntimePath = RuntimeEnvironment.GetRuntimeDirectory();
            var dotnetRoot = Path.GetFullPath(Path.Combine(dotnetRuntimePath, "../../../"));
            return dotnetRoot;
        }

        private static IReadOnlyCollection<MetadataReference> CreateDefaultReferences(string assemblyPath, IReadOnlyCollection<string> assemblies)
        {
            return assemblies
                .AsParallel()
                .Select(dll =>
                {
                    string fullReferencePath = Path.Combine(assemblyPath, dll);
                    string fullDocumentationPath = Path.ChangeExtension(fullReferencePath, ".xml");

                    if (!IsManagedAssembly(fullReferencePath))
                        return null;

                    return File.Exists(fullDocumentationPath)
                        ? MetadataReference.CreateFromFile(fullReferencePath, documentation: XmlDocumentationProvider.CreateFromFile(fullDocumentationPath))
                        : MetadataReference.CreateFromFile(fullReferencePath);
                })
                .WhereNotNull()
                .ToList();
        }

        private static bool IsManagedAssembly(string assemblyPath)
        {
            try
            {
                _ = AssemblyName.GetAssemblyName(assemblyPath);
                return true;
            }
            catch (FileNotFoundException) { return false; }
            catch (BadImageFormatException) { return false; }
        }
    }
}
