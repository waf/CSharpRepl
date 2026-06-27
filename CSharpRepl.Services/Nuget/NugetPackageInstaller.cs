// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using CSharpRepl.Services.Roslyn.References;
using Microsoft.CodeAnalysis;
using NuGet.Commands;
using NuGet.Common;
using NuGet.Configuration;
using NuGet.Frameworks;
using NuGet.LibraryModel;
using NuGet.Packaging;
using NuGet.Packaging.Signing;
using NuGet.ProjectModel;
using NuGet.Protocol;
using NuGet.Protocol.Core.Types;
using NuGet.Versioning;

namespace CSharpRepl.Services.Nuget;

/// <summary>
/// Installs nuget packages requested via <c>#r "nuget: Id, Version"</c> by running a real, in-process
/// NuGet <see cref="RestoreCommand"/> - the same engine <c>dotnet restore</c> uses.
/// <para>
/// Every <c>#r "nuget:"</c> in a session accumulates into a single logical project, and each install
/// re-restores the whole set. This means transitive versions are unified across packages exactly the way
/// a built app's restore unifies them (highest-applicable, honoring version ranges), instead of the
/// previous per-package, min-version dependency walk which could leave two versions of the same assembly
/// in the reference set. https://github.com/waf/CSharpRepl/issues/355
/// </para>
/// </summary>
internal sealed class NugetPackageInstaller
{
    private readonly ConsoleNugetLogger logger;
    private readonly bool usePrereleaseNugets;
    private readonly AssemblyReferenceService? referenceAssemblyService;

    // Owns runtime loading; pins each resolved package assembly to one runtime instance (ReplAssemblyLoader.Pin).
    // Null when there's no script engine to bind into (e.g. unit tests that only assert on the returned references).
    private readonly ReplAssemblyLoader? assemblyLoader;

    // The session's accumulated top-level package references (the equivalent of a project's <PackageReference>s).
    // Each #r adds/updates an entry, and we restore the whole set so versions unify across packages.
    private readonly Dictionary<string, VersionRange> topLevelPackages = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim restoreLock = new(1, 1);

    // A per-instance obj directory for the restore (the assets/cache file are written here). RestoreCommand
    // requires the output directory to exist even though we read the resulting lock file in-memory.
    private readonly string restoreOutputPath =
        Path.Combine(Path.GetTempPath(), "csharprepl-restore", Guid.NewGuid().ToString("N"));

    public NugetPackageInstaller(IConsoleService console, Configuration configuration, AssemblyReferenceService? referenceAssemblyService = null, ReplAssemblyLoader? assemblyLoader = null)
    {
        this.logger = new ConsoleNugetLogger(console, configuration);
        this.usePrereleaseNugets = configuration.UsePrereleaseNugets;
        this.referenceAssemblyService = referenceAssemblyService;
        this.assemblyLoader = assemblyLoader;
    }

    public async Task<ImmutableArray<PortableExecutableReference>> InstallAsync(
        string packageId,
        string? version = null,
        CancellationToken cancellationToken = default)
    {
        await restoreLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        var hadPrevious = topLevelPackages.TryGetValue(packageId, out var previousRange);
        try
        {
            topLevelPackages[packageId] = ParseRequestedRange(version);

            var outcome = await logger
                .WithStatusAsync(packageId, () => RestoreAsync(packageId, cancellationToken))
                .ConfigureAwait(false);
            if (outcome.Succeeded)
            {
                // The package may legitimately contribute no references of its own (e.g. it's provided by the
                // shared framework, or it's a metapackage), so success is the restore succeeding, not a count.
                var resolved = outcome.ResolvedVersion is null ? packageId : $"{packageId}.{outcome.ResolvedVersion}";
                logger.LogInformationSummary($"Adding references for '{resolved}'");
                logger.LogFinish($"Package '{resolved}' was successfully installed.", success: true);
                return outcome.References;
            }

            // Don't let a bad package id poison every subsequent restore in the session.
            RevertTopLevel(packageId, hadPrevious, previousRange);
            logger.LogFinish($"Could not restore package '{packageId}'.", success: false);
            return [];
        }
        catch (Exception ex)
        {
            RevertTopLevel(packageId, hadPrevious, previousRange);
            logger.LogFinish($"Could not restore package '{packageId}'. Error: {ex.Message}", success: false);
            return [];
        }
        finally
        {
            restoreLock.Release();
        }
    }

    private void RevertTopLevel(string packageId, bool hadPrevious, VersionRange? previousRange)
    {
        if (hadPrevious) topLevelPackages[packageId] = previousRange!;
        else topLevelPackages.Remove(packageId);
    }

    /// <summary>
    /// A specified version (e.g. <c>#r "nuget: X, 1.2.3"</c>) is treated as a minimum, the same as a project's
    /// <c>&lt;PackageReference Version="1.2.3" /&gt;</c>, so it can unify upward when another package needs more.
    /// No version means "latest", expressed as a floating range.
    /// </summary>
    private VersionRange ParseRequestedRange(string? version)
        => string.IsNullOrWhiteSpace(version)
            ? VersionRange.Parse(usePrereleaseNugets ? "*-*" : "*")
            : VersionRange.Parse(version);

    private readonly record struct RestoreOutcome(bool Succeeded, ImmutableArray<PortableExecutableReference> References, string? ResolvedVersion)
    {
        public static readonly RestoreOutcome Failed = new(false, [], null);
    }

    private async Task<RestoreOutcome> RestoreAsync(string requestedPackageId, CancellationToken cancellationToken)
    {
        if (!NugetHelper.TryGetCurrentFramework(out var targetFramework))
        {
            logger.LogError("Unable to determine the current target framework.");
            return RestoreOutcome.Failed;
        }

        logger.LogInformation("");
        logger.LogInformation(Environment.NewLine + $"Installing package '{requestedPackageId}'");
        var runtimeIdentifier = RuntimeInformation.RuntimeIdentifier;
        var settings = ReadSettings();
        var globalPackagesFolder = SettingsUtility.GetGlobalPackagesFolder(settings);
        var fallbackFolders = SettingsUtility.GetFallbackPackageFolders(settings).ToList();
        var sourceRepositories = GetSourceRepositories(settings);

        Directory.CreateDirectory(restoreOutputPath);

        var packageSpec = BuildPackageSpec(targetFramework, globalPackagesFolder, fallbackFolders, settings);

        using var sourceCacheContext = new SourceCacheContext();
        var providers = new RestoreCommandProvidersCache().GetOrCreate(
            globalPackagesFolder,
            fallbackFolders,
            sourceRepositories,
            sourceCacheContext,
            logger);

        var request = new RestoreRequest(
            packageSpec,
            providers,
            sourceCacheContext,
            ClientPolicyContext.GetClientPolicy(settings, logger),
            PackageSourceMapping.GetPackageSourceMapping(settings),
            logger,
            new LockFileBuilderCache())
        {
            ProjectStyle = ProjectStyle.PackageReference,
            AllowNoOp = false,
        };
        request.RequestedRuntimes.Add(runtimeIdentifier);

        var dependencyGraph = new DependencyGraphSpec();
        dependencyGraph.AddProject(packageSpec);
        dependencyGraph.AddRestore(packageSpec.RestoreMetadata.ProjectUniqueName);
        request.DependencyGraphSpec = dependencyGraph;

        var result = await new RestoreCommand(request).ExecuteAsync(cancellationToken).ConfigureAwait(false);
        if (!result.Success)
        {
            foreach (var message in result.LogMessages.Where(m => m.Level >= LogLevel.Warning))
            {
                logger.LogError(message.Message);
            }
            return RestoreOutcome.Failed;
        }

        var runtimeTarget = result.LockFile.GetTarget(targetFramework, runtimeIdentifier);
        if (runtimeTarget is null)
        {
            logger.LogError($"Restore produced no assets for {targetFramework.GetShortFolderName()}/{runtimeIdentifier}.");
            return RestoreOutcome.Failed;
        }

        var references = CollectReferences(result.LockFile, runtimeTarget);
        assemblyLoader?.RegisterPinned(references);
        var resolvedVersion = runtimeTarget.Libraries
            .FirstOrDefault(l => string.Equals(l.Name, requestedPackageId, StringComparison.OrdinalIgnoreCase))?
            .Version?.ToNormalizedString();
        return new RestoreOutcome(Succeeded: true, references, resolvedVersion);
    }

    private PackageSpec BuildPackageSpec(
        NuGetFramework targetFramework,
        string globalPackagesFolder,
        List<string> fallbackFolders,
        ISettings settings)
    {
        var dependencies = topLevelPackages
            .Select(p => new LibraryDependency
            {
                LibraryRange = new LibraryRange(p.Key, p.Value, LibraryDependencyTarget.Package),
            })
            .ToImmutableArray();

        var targetFrameworkInformation = new TargetFrameworkInformation
        {
            FrameworkName = targetFramework,
            Dependencies = dependencies,
            // Point the restore at a RID graph so RID-specific assets (runtimes/<rid>/lib, runtimes/<rid>/native)
            // are selected for the current runtime, e.g. System.Management's win build. Without this the restore
            // only ever picks the RID-agnostic lib/ assets. We reuse the runtime.json the tool already ships.
            RuntimeIdentifierGraphPath = NugetHelper.RuntimeGraphPath,
        };

        var projectPath = Path.Combine(restoreOutputPath, "csharprepl.csproj");
        return new PackageSpec(new List<TargetFrameworkInformation> { targetFrameworkInformation })
        {
            Name = "csharprepl",
            FilePath = projectPath,
            // The shipped runtime.json lets the restore resolve RID-specific (runtimes/<rid>/...) assets,
            // mirroring how the framework's RID graph fans out (e.g. win-x64 -> win -> any).
            RuntimeGraph = NugetHelper.GetRuntimeGraph(logger.LogError),
            RestoreMetadata = new ProjectRestoreMetadata
            {
                ProjectStyle = ProjectStyle.PackageReference,
                ProjectName = "csharprepl",
                ProjectUniqueName = projectPath,
                ProjectPath = projectPath,
                OutputPath = restoreOutputPath,
                CacheFilePath = Path.Combine(restoreOutputPath, "csharprepl.csproj.nuget.cache"),
                ConfigFilePaths = settings.GetConfigFilePaths(),
                Sources = SettingsUtility.GetEnabledSources(settings).ToList(),
                PackagesPath = globalPackagesFolder,
                FallbackFolders = fallbackFolders,
                OriginalTargetFrameworks = new List<string> { targetFramework.GetShortFolderName() },
            },
        };
    }

    /// <summary>
    /// Reads the resolved, unified set of assemblies out of the restore's lock file. We take the runtime
    /// (implementation) assemblies from the RID-specific target - that's both what the REPL loads at runtime
    /// and what historically backed compilation (the implementation-vs-reference-assembly choice in
    /// <see cref="AssemblyReferenceService"/>). Native asset directories are registered for p/invoke (#375).
    /// </summary>
    private ImmutableArray<PortableExecutableReference> CollectReferences(LockFile lockFile, LockFileTarget runtimeTarget)
    {
        var pathResolver = new FallbackPackagePathResolver(
            lockFile.PackageFolders.Select(f => f.Path).First(),
            lockFile.PackageFolders.Skip(1).Select(f => f.Path));

        var references = ImmutableArray.CreateBuilder<PortableExecutableReference>();
        foreach (var library in runtimeTarget.Libraries.Where(l => string.Equals(l.Type, "package", StringComparison.OrdinalIgnoreCase)))
        {
            if (library.Name is null || library.Version is null)
                continue;

            var packageDirectory = pathResolver.GetPackageDirectory(library.Name, library.Version);
            if (packageDirectory is null)
                continue;

            // Prefer runtime assemblies; fall back to compile-time assemblies for ref-only packages.
            var assemblyItems = library.RuntimeAssemblies.Count > 0 ? library.RuntimeAssemblies : library.CompileTimeAssemblies;
            foreach (var item in assemblyItems)
            {
                var fullPath = ToFullPath(packageDirectory, item.Path);
                if (fullPath is not null)
                {
                    references.Add(MetadataReference.CreateFromFile(fullPath));
                }
            }

            // Native libraries aren't metadata references, but their directory must be on the p/invoke search
            // path or the managed binding that needs them fails to load at runtime. https://github.com/waf/CSharpRepl/issues/375
            foreach (var native in library.NativeLibraries)
            {
                var fullPath = ToFullPath(packageDirectory, native.Path);
                var directory = fullPath is null ? null : Path.GetDirectoryName(fullPath);
                if (directory is not null)
                {
                    referenceAssemblyService?.AddNativeSearchDirectory(directory);
                }
            }
        }

        return references.ToImmutable();
    }

    /// <summary>
    /// Lock-file item paths are forward-slash relative paths (e.g. <c>lib/net8.0/Foo.dll</c>). <c>_._</c>
    /// placeholders mark "compatible but no assets" and must be skipped.
    /// </summary>
    private static string? ToFullPath(string packageDirectory, string itemPath)
    {
        if (string.IsNullOrEmpty(itemPath) || Path.GetFileName(itemPath) == "_._")
            return null;

        var normalized = itemPath.Replace('/', Path.DirectorySeparatorChar);
        return Path.GetFullPath(Path.Combine(packageDirectory, normalized));
    }

    private static IReadOnlyList<SourceRepository> GetSourceRepositories(ISettings settings)
        => new SourceRepositoryProvider(new PackageSourceProvider(settings), Repository.Provider.GetCoreV3())
            .GetRepositories()
            .ToList();

    private static ISettings ReadSettings()
    {
        var currentDirectory = Directory.GetCurrentDirectory();
        return File.Exists(Path.Combine(currentDirectory, Settings.DefaultSettingsFileName))
            ? Settings.LoadSpecificSettings(currentDirectory, Settings.DefaultSettingsFileName)
            : Settings.LoadDefaultSettings(currentDirectory);
    }
}
