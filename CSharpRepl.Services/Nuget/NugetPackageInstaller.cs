// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using NuGet.Client;
using NuGet.Configuration;
using NuGet.ContentModel;
using NuGet.Frameworks;
using NuGet.PackageManagement;
using NuGet.Packaging;
using NuGet.Packaging.Core;
using NuGet.Packaging.PackageExtraction;
using NuGet.Packaging.Signing;
using NuGet.ProjectManagement;
using NuGet.Protocol.Core.Types;
using NuGet.Resolver;
using NuGet.RuntimeModel;
using NuGet.Versioning;
using PrettyPrompt.Consoles;

namespace CSharpRepl.Services.Nuget;

internal sealed class NugetPackageInstaller
{
    private const string RuntimeFileName = "runtime.json";

    private static readonly Mutex MultipleNuspecPatchMutex = new(false, $"CSharpRepl_{nameof(MultipleNuspecPatchMutex)}");

    private readonly ConsoleNugetLogger logger;

    public NugetPackageInstaller(IConsole console, Configuration configuration)
    {
        this.logger = new ConsoleNugetLogger(console, configuration);
    }

    public async Task<ImmutableArray<PortableExecutableReference>> InstallAsync(
        string packageId,
        string? version = null,
        CancellationToken cancellationToken = default)
    {
        logger.Reset();

        try
        {
            ISettings settings = ReadSettings();
            var targetFramework = GetCurrentFramework();
            var nuGetProject = CreateFolderProject(Path.Combine(Configuration.ApplicationDirectory, "packages"));
            var sourceRepositoryProvider = new SourceRepositoryProvider(new PackageSourceProvider(settings), Repository.Provider.GetCoreV3());
            var packageManager = CreatePackageManager(settings, nuGetProject, sourceRepositoryProvider);

            using var sourceCacheContext = new SourceCacheContext();

            var resolutionContext = new ResolutionContext(
                DependencyBehavior.Lowest,
                includePrelease: true, includeUnlisted: true,
                VersionConstraints.None, new GatherCache(), sourceCacheContext
            );

            var primarySourceRepositories = sourceRepositoryProvider.GetRepositories();
            var packageIdentity = string.IsNullOrEmpty(version)
                ? await QueryLatestPackageVersion(packageId, nuGetProject, resolutionContext, primarySourceRepositories, cancellationToken)
                : new PackageIdentity(packageId, new NuGetVersion(version));

            if (!packageIdentity.HasVersion)
            {
                logger.LogFinish($"Could not find package '{packageIdentity}'", success: false);
                return ImmutableArray<PortableExecutableReference>.Empty;
            }

            var skipInstall = nuGetProject.PackageExists(packageIdentity);
            if (!skipInstall)
            {
                await DownloadPackageAsync(packageIdentity, packageManager, resolutionContext, primarySourceRepositories, settings, cancellationToken);
            }

            logger.LogInformationSummary($"Adding references for '{packageIdentity}'");
            var references = await GetAssemblyReferenceWithDependencies(targetFramework, nuGetProject, packageIdentity, cancellationToken);

            logger.LogFinish($"Package '{packageIdentity}' was successfully installed.", success: true);
            return references;
        }
        catch (Exception ex)
        {
            logger.LogFinish($"Could not find package '{packageId}'. Error: {ex}", success: false);
            return ImmutableArray<PortableExecutableReference>.Empty;
        }
    }

    private async Task<ImmutableArray<PortableExecutableReference>> GetAssemblyReferenceWithDependencies(
        NuGetFramework targetFramework,
        FolderNuGetProject nuGetProject,
        PackageIdentity packageIdentity,
        CancellationToken cancellationToken)
    {
        var runtimeGraph = GetRuntimeGraph();
        var managedCodeConventions = new ManagedCodeConventions(runtimeGraph);
        var referencesPerPackage = await GetDependencies(targetFramework, nuGetProject, packageIdentity, managedCodeConventions, cancellationToken);
        return referencesPerPackage.Values.SelectMany(r => r).ToImmutableArray();
    }

    private static async Task<Dictionary<PackageIdentity, List<PortableExecutableReference>>> GetDependencies(
        NuGetFramework targetFramework,
        FolderNuGetProject nuGetProject,
        PackageIdentity packageIdentity,
        ManagedCodeConventions managedCodeConventions,
        CancellationToken cancellationToken)
    {
        var aggregatedReferences = new Dictionary<PackageIdentity, List<PortableExecutableReference>>();
        await GetDependencies(targetFramework, nuGetProject, packageIdentity, managedCodeConventions, aggregatedReferences, cancellationToken);
        return aggregatedReferences;
    }

    private static async Task GetDependencies(
        NuGetFramework targetFramework,
        FolderNuGetProject nuGetProject,
        PackageIdentity packageIdentity,
        ManagedCodeConventions managedCodeConventions,
        Dictionary<PackageIdentity, List<PortableExecutableReference>> aggregatedReferences,
        CancellationToken cancellationToken)
    {
        var installedPath = new DirectoryInfo(Path.Combine(nuGetProject.Root, packageIdentity.ToString()));
        if (!installedPath.Exists)
            return;

        lock (aggregatedReferences)
        {
            if (aggregatedReferences.ContainsKey(packageIdentity))
                return;
        }

        var reader = new PackageFolderReader(installedPath);
        var collection = new ContentItemCollection();
        collection.Load(await reader.GetFilesAsync(cancellationToken));
        string[] FindDlls(NuGetFramework framework) =>
            collection.FindBestItemGroup(
                managedCodeConventions.Criteria.ForFrameworkAndRuntime(framework, RuntimeInformation.RuntimeIdentifier),
                managedCodeConventions.Patterns.RuntimeAssemblies)
            ?.Items
            .Select(i => i.Path)
            .Where(p => p.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
            .ToArray()
            ?? Array.Empty<string>();

        var supportedFrameworks =
            (await reader.GetSupportedFrameworksAsync(cancellationToken))
            .Where(f => FindDlls(f).Any()) //Not all supported frameworks contains dlls. E.g. Microsoft.CSharp.4.7.0\lib\netcoreapp2.0 contains only empty file '_._'.
            .ToList();

        var frameworkReducer = new FrameworkReducer();
        var framework = frameworkReducer.GetNearest(targetFramework, supportedFrameworks);
        var dlls = FindDlls(framework)
                .Select(path => MetadataReference.CreateFromFile(Path.GetFullPath(Path.Combine(nuGetProject.Root, packageIdentity.ToString(), path)))) //GetFullPath will normalize separators
                .ToList();
        lock (aggregatedReferences)
        {
            aggregatedReferences[packageIdentity] = dlls;
        }

        CheckAndFixMultipleNuspecFilesExistance(installedPath.FullName);
        var dependencyGroup =
            (await reader.GetPackageDependenciesAsync(cancellationToken))
            .FirstOrDefault(g => g.TargetFramework == framework);

        if (dependencyGroup is null)
            return;

        var firstLevelDependencies = dependencyGroup
            .Packages
            .Select(p => new PackageIdentity(p.Id, p.VersionRange.MinVersion));

        await Task.WhenAll(
            firstLevelDependencies.Select(p => GetDependencies(targetFramework, nuGetProject, p, managedCodeConventions, aggregatedReferences, cancellationToken))
        );
    }

    private static NuGetFramework GetCurrentFramework()
    {
        var assembly = Assembly.GetEntryAssembly();
        if (assembly is null ||
            assembly.Location.EndsWith("testhost.dll", StringComparison.OrdinalIgnoreCase)) //for unit tests (testhost.dll targets netcoreapp2.1 instead of net6.0)
        {
            assembly = Assembly.GetExecutingAssembly();
        }

        var targetFrameworkAttribute = assembly.GetCustomAttribute<TargetFrameworkAttribute>();
        return NuGetFramework.Parse(targetFrameworkAttribute?.FrameworkName);
    }

    private async Task DownloadPackageAsync(
        PackageIdentity packageIdentity,
        NuGetPackageManager packageManager,
        ResolutionContext resolutionContext,
        IEnumerable<SourceRepository> primarySourceRepositories,
        ISettings settings,
        CancellationToken cancellationToken)
    {
        var clientPolicyContext = ClientPolicyContext.GetClientPolicy(settings, logger);
        var projectContext = new ConsoleProjectContext(logger)
        {
            PackageExtractionContext = new PackageExtractionContext(
                PackageSaveMode.Defaultv3,
                PackageExtractionBehavior.XmlDocFileSaveMode,
                clientPolicyContext,
                logger)
            {
                CopySatelliteFiles = false
            }
        };

        await packageManager.InstallPackageAsync(packageManager.PackagesFolderNuGetProject,
            packageIdentity, resolutionContext, projectContext,
            primarySourceRepositories, Array.Empty<SourceRepository>(), cancellationToken);
    }

    private async Task<PackageIdentity> QueryLatestPackageVersion(
        string packageId,
        FolderNuGetProject nuGetProject,
        ResolutionContext resolutionContext,
        IEnumerable<SourceRepository> primarySourceRepositories,
        CancellationToken cancellationToken)
    {
        var resolvePackage = await NuGetPackageManager.GetLatestVersionAsync(
            packageId, nuGetProject,
            resolutionContext, primarySourceRepositories,
            logger, cancellationToken
        );
        return new PackageIdentity(packageId, resolvePackage.LatestVersion);
    }

    private static NuGetPackageManager CreatePackageManager(
        ISettings settings,
        FolderNuGetProject nuGetProject,
        SourceRepositoryProvider sourceRepositoryProvider)
    {
        var packageManager = new NuGetPackageManager(sourceRepositoryProvider, settings, nuGetProject.Root)
        {
            PackagesFolderNuGetProject = nuGetProject
        };
        return packageManager;
    }

    private static FolderNuGetProject CreateFolderProject(string directory)
    {
        string projectRoot = Path.GetFullPath(directory);
        Directory.CreateDirectory(projectRoot);
        if (!Directory.Exists(projectRoot)) Directory.CreateDirectory(projectRoot);
        var nuGetProject = new FolderNuGetProject(
            projectRoot,
            packagePathResolver: new PackagePathResolver(projectRoot)
        );
        return nuGetProject;
    }

    private static ISettings ReadSettings()
    {
        var curDir = Directory.GetCurrentDirectory();
        ISettings settings = File.Exists(Path.Combine(curDir, Settings.DefaultSettingsFileName))
            ? Settings.LoadSpecificSettings(curDir, Settings.DefaultSettingsFileName)
            : Settings.LoadDefaultSettings(curDir);
        return settings;
    }

    private RuntimeGraph GetRuntimeGraph()
    {
        var dir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
        if (dir != null)
        {
            var path = Path.Combine(dir, RuntimeFileName);
            if (File.Exists(path))
            {
                using var stream = File.OpenRead(path);
                return JsonRuntimeFormat.ReadRuntimeGraph(stream);
            }
        }
        logger.LogError($"Cannot find '{RuntimeFileName}' in '{dir}'");
        return new RuntimeGraph();
    }

    /// <summary>
    /// This is a patch for https://github.com/waf/CSharpRepl/issues/52.
    /// The problem emerges on systems with case-sensitive file system.
    /// There can be multiple nuspec files differing only in name casing in the package folder.lder.
    /// Not sure why this happens (I suspect there is a bug in NuGet.PackageManagement).
    /// </summary>
    private static void CheckAndFixMultipleNuspecFilesExistance(string packageDirectoryPath)
    {
        MultipleNuspecPatchMutex.WaitOne();
        try
        {
            var nuspecFileGroups = Directory.EnumerateFiles(packageDirectoryPath)
                .Where(f => f.EndsWith(".nuspec", StringComparison.OrdinalIgnoreCase))
                .GroupBy(f => f, StringComparer.OrdinalIgnoreCase);

            foreach (var nuspecsWithSameName in nuspecFileGroups)
            {
                foreach (var duplicateNuspec in nuspecsWithSameName.Skip(1))
                {
                    File.Delete(duplicateNuspec);
                }
            }
        }
        finally
        {
            MultipleNuspecPatchMutex.ReleaseMutex();
        }
    }
}