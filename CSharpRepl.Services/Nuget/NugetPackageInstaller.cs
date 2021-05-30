#region License Header
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.
#endregion
using Microsoft.CodeAnalysis;
using NuGet.Configuration;
using NuGet.Frameworks;
using NuGet.PackageManagement;
using NuGet.Packaging;
using NuGet.Packaging.Core;
using NuGet.Packaging.PackageExtraction;
using NuGet.Packaging.Signing;
using NuGet.ProjectManagement;
using NuGet.Protocol.Core.Types;
using NuGet.Resolver;
using NuGet.Versioning;
using PrettyPrompt.Consoles;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Versioning;
using System.Threading;
using System.Threading.Tasks;

namespace Sharply.Services.Nuget
{
    public class NugetPackageInstaller
    {
        private readonly ConsoleNugetLogger logger;

        public NugetPackageInstaller(IConsole console)
        {
            this.logger = new ConsoleNugetLogger(console);
        }

        public async Task<ImmutableArray<PortableExecutableReference>> InstallAsync(
            string packageId, string version = null, CancellationToken cancellationToken = default)
        {
            ISettings settings = ReadSettings();
            var frameworkVersion = GetCurrentFramework();
            var nuGetProject = CreateFolderProject("./packages");
            var sourceRepositoryProvider = new SourceRepositoryProvider(new PackageSourceProvider(settings), Repository.Provider.GetCoreV3());
            var packageManager = CreatePackageManager(settings, nuGetProject, sourceRepositoryProvider);

            using var sourceCacheContext = new SourceCacheContext();

            var resolutionContext = new ResolutionContext(
                DependencyBehavior.Lowest,
                includePrelease: true, includeUnlisted: true,
                VersionConstraints.None, new GatherCache(), sourceCacheContext
            );

            IEnumerable<SourceRepository> primarySourceRepositories = sourceRepositoryProvider.GetRepositories();
            PackageIdentity packageIdentity = string.IsNullOrEmpty(version)
                ? await QueryLatestPackageVersion(packageId, nuGetProject, resolutionContext, primarySourceRepositories, cancellationToken)
                : new PackageIdentity(packageId, new NuGetVersion(version));

            if (!packageIdentity.HasVersion)
            {
                throw new NuGetResolverException($@"Could not find package ""{packageIdentity}""");
            }

            var skipInstall = nuGetProject.PackageExists(packageIdentity);
            if (!skipInstall)
            {
                await DownloadPackageAsync(packageIdentity, packageManager, resolutionContext, primarySourceRepositories, settings, cancellationToken);
            }

            var references = await GetAssemblyReferenceWithDependencies(frameworkVersion, nuGetProject, packageIdentity, cancellationToken);
            if(references.Any())
            {
                logger.LogInformationSummary("Adding references for " + packageIdentity);
            }
            return references;
        }

        private async Task<ImmutableArray<PortableExecutableReference>> GetAssemblyReferenceWithDependencies(NuGetFramework frameworkVersion, FolderNuGetProject nuGetProject, PackageIdentity packageIdentity, CancellationToken cancellationToken)
        {
            var packages = await GetDependencies(frameworkVersion, nuGetProject, packageIdentity, cancellationToken);

            // get the filenames of everything under the "lib" directory, for both the provided nuget package and all its dependent packages.
            var packageContents = await Task.WhenAll(packages
                .Select(async package =>
                {
                    var libs = await package.Value.GetLibItemsAsync(cancellationToken);
                    package.Value.Dispose();
                    return (package: package.Key.ToString(), libs);
                })
            );

            // filter down to only the dependencies that are compatible with the current framework.
            // e.g. netstandard2.1 packages are compatible with net5 applications.
            var frameworkCompatibleContents = packageContents
                .Where(contents => contents.libs.Any())
                .SelectMany(contents => contents.libs
                    .Last(lib => DefaultCompatibilityProvider.Instance.IsCompatible(
                        frameworkVersion,
                        lib.TargetFramework
                    ))
                    .Items
                    .Where(filepath => Path.GetExtension(filepath) == ".dll")
                    .Select(filepath => (contents.package, filepath))
                );

            var references = frameworkCompatibleContents
                .Select(content => MetadataReference
                    .CreateFromFile(
                        Path.Combine(nuGetProject.Root, content.package, content.filepath)
                    )
                )
                .ToImmutableArray();

            return references;
        }

        private static async Task<Dictionary<PackageIdentity, PackageFolderReader>> GetDependencies(
            NuGetFramework frameworkVersion, FolderNuGetProject nuGetProject, PackageIdentity packageIdentity,
            CancellationToken cancellationToken)
        {
            var dependencies = new Dictionary<PackageIdentity, PackageFolderReader>();
            await GetDependencies(frameworkVersion, nuGetProject, packageIdentity, dependencies, cancellationToken);
            return dependencies;
        }

        private static async Task GetDependencies(NuGetFramework frameworkVersion, FolderNuGetProject nuGetProject, PackageIdentity packageIdentity, Dictionary<PackageIdentity, PackageFolderReader> aggregatedDependencies, CancellationToken cancellationToken)
        {
            aggregatedDependencies ??= new Dictionary<PackageIdentity, PackageFolderReader>();

            var installedPath = new DirectoryInfo(Path.Combine(nuGetProject.Root, packageIdentity.ToString()));
            if (aggregatedDependencies.ContainsKey(packageIdentity) || !installedPath.Exists)
                return;

            var reader = new PackageFolderReader(installedPath);
            aggregatedDependencies[packageIdentity] = reader;

            var dependencyGroup = await reader.GetPackageDependenciesAsync(cancellationToken);
            var firstLevelDependencies = dependencyGroup
                .Last(group => DefaultCompatibilityProvider.Instance.IsCompatible(frameworkVersion, group.TargetFramework))
                .Packages
                .Select(p => new PackageIdentity(p.Id, p.VersionRange.MinVersion));

            await Task.WhenAll(
                firstLevelDependencies.Select(p => GetDependencies(frameworkVersion, nuGetProject, p, aggregatedDependencies, cancellationToken))
            );
        }

        private static NuGetFramework GetCurrentFramework() => 
            NuGetFramework.Parse(
                Assembly
                    .GetEntryAssembly()?
                    .GetCustomAttribute<TargetFrameworkAttribute>()?
                    .FrameworkName
            );

        private async Task DownloadPackageAsync(
            PackageIdentity packageIdentity, NuGetPackageManager packageManager,
            ResolutionContext resolutionContext, IEnumerable<SourceRepository> primarySourceRepositories,
            ISettings settings, CancellationToken cancellationToken)
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
            string packageId, FolderNuGetProject nuGetProject,
            ResolutionContext resolutionContext, IEnumerable<SourceRepository> primarySourceRepositories,
            CancellationToken cancellationToken)
        {
            var resolvePackage = await NuGetPackageManager.GetLatestVersionAsync(
                packageId, nuGetProject,
                resolutionContext, primarySourceRepositories,
                logger, cancellationToken
            );
            PackageIdentity packageIdentity = new PackageIdentity(packageId, resolvePackage.LatestVersion);
            return packageIdentity;
        }

        private static NuGetPackageManager CreatePackageManager(ISettings settings, FolderNuGetProject nuGetProject, SourceRepositoryProvider sourceRepositoryProvider)
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
    }
}
