// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Microsoft.CodeAnalysis;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;

namespace CSharpRepl.Services.Roslyn
{
    /// <remarks>
    /// Useful notes https://github.com/dotnet/roslyn/blob/main/docs/wiki/Runtime-code-generation-using-Roslyn-compilations-in-.NET-Core-App.md
    /// </remarks>
    class ReferenceAssemblyService
    {
        private readonly Dictionary<string, MetadataReference> cachedMetadataReferences;
        private readonly HashSet<MetadataReference> loadedReferenceAssemblies;
        private readonly HashSet<MetadataReference> loadedImplementationAssemblies;
        private readonly HashSet<string> referenceAssemblyPaths;
        private readonly HashSet<string> implementationAssemblyPaths;

        public IReadOnlySet<string> ImplementationAssemblyPaths => implementationAssemblyPaths;
        public IReadOnlySet<MetadataReference> LoadedImplementationAssemblies => loadedImplementationAssemblies;
        public IReadOnlySet<MetadataReference> LoadedReferenceAssemblies => loadedReferenceAssemblies;
        public IReadOnlyCollection<string> DefaultUsings { get; }

        public ReferenceAssemblyService(Configuration config)
        {
            this.referenceAssemblyPaths = new();
            this.implementationAssemblyPaths = new();
            this.cachedMetadataReferences = new();
            this.loadedReferenceAssemblies = new HashSet<MetadataReference>(new EqualityComparerFunc<MetadataReference>(
                (r1, r2) => r1.Display.Equals(r2.Display),
                (r1) => r1.Display.GetHashCode()
            ));
            this.loadedImplementationAssemblies = new HashSet<MetadataReference>(new EqualityComparerFunc<MetadataReference>(
                (r1, r2) => r1.Display.Equals(r2.Display),
                (r1) => r1.Display.GetHashCode()
            ));
            this.DefaultUsings = new[] {
                    "System", "System.IO", "System.Collections.Generic",
                    "System.Linq", "System.Net.Http",
                    "System.Text", "System.Threading.Tasks"
                }
                .Concat(config.Usings)
                .ToList();

            var (framework, version) = GetDesiredFrameworkVersion(config.Framework);
            var sharedFrameworks = GetSharedFrameworkConfiguration(framework, version);
            LoadSharedFrameworkConfiguration(sharedFrameworks);
        }

        internal IReadOnlyCollection<MetadataReference> EnsureReferenceAssemblyWithDocumentation(IReadOnlyCollection<MetadataReference> references)
        {
            loadedReferenceAssemblies.UnionWith(
                references.Select(suppliedReference => EnsureReferenceAssembly(suppliedReference)).Where(reference => reference is not null)
            );
            return loadedReferenceAssemblies;
        }

        internal void AddImplementationAssemblyReferences(IEnumerable<MetadataReference> references)
        {
            this.implementationAssemblyPaths.UnionWith(references.Select(r => Path.GetDirectoryName(r.Display)));
            this.loadedImplementationAssemblies.UnionWith(references);
        }

        // a bit of a mismatch -- the scripting APIs use implementation assemblies only. The general workspace/project roslyn APIs use the reference
        // assemblies. Just to ensure that we don't accidentally use an implementation assembly with the roslyn APIs, do some "best-effort" conversion
        // here. If it's a reference assembly, pass it through unchanged. If it's an implementation assembly, try to locate the corresponding reference assembly.
        private MetadataReference EnsureReferenceAssembly(MetadataReference reference)
        {
            string suppliedAssemblyPath = reference.Display;
            if (cachedMetadataReferences.TryGetValue(suppliedAssemblyPath, out MetadataReference cachedReference))
            {
                return cachedReference;
            }

            // it's already a reference assembly, just cache it and use it.
            if (referenceAssemblyPaths.Any(path => reference.Display.StartsWith(path)))
            {
                cachedMetadataReferences[suppliedAssemblyPath] = reference;
                return reference;
            }

            // it's probably an implementation assembly, find the corresponding reference assembly and documentation if we can.

            var suppliedAssemblyName = Path.GetFileName(suppliedAssemblyPath);
            var assembly = referenceAssemblyPaths
                .Select(path => Path.Combine(path, suppliedAssemblyName))
                .FirstOrDefault(potentialReferencePath => File.Exists(potentialReferencePath))
                ?? suppliedAssemblyPath;

            var potentialDocumentationPath = Path.ChangeExtension(assembly, ".xml");
            var documentation = File.Exists(potentialDocumentationPath)
                ? XmlDocumentationProvider.CreateFromFile(potentialDocumentationPath)
                : null;

            var completeMetadataReference = MetadataReference.CreateFromFile(assembly, documentation: documentation);
            cachedMetadataReferences[suppliedAssemblyPath] = completeMetadataReference;
            return completeMetadataReference;
        }

        public void LoadSharedFrameworkConfiguration(string framework, Version version)
        {
            var sharedFrameworks = GetSharedFrameworkConfiguration(framework, version);
            LoadSharedFrameworkConfiguration(sharedFrameworks);
        }

        public void LoadSharedFrameworkConfiguration(SharedFramework[] sharedFrameworks)
        {
            this.referenceAssemblyPaths.UnionWith(sharedFrameworks.Select(framework => framework.ReferencePath));
            this.implementationAssemblyPaths.UnionWith(sharedFrameworks.Select(framework => framework.ImplementationPath));
            this.loadedReferenceAssemblies.UnionWith(sharedFrameworks.SelectMany(framework => framework.ReferenceAssemblies));
            this.loadedImplementationAssemblies.UnionWith(sharedFrameworks.SelectMany(framework => framework.ImplementationAssemblies));
        }

        public SharedFramework[] GetSharedFrameworkConfiguration(string framework, Version version)
        {
            var referencePath = GetCurrentAssemblyReferencePath(framework, version);
            var implementationPath = GetCurrentAssemblyImplementationPath(framework, version);

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
                    new SharedFramework(referencePath, implementationPath, referenceDlls, implementationDlls)
                },
                _ => GetSharedFrameworkConfiguration(SharedFramework.NetCoreApp, version)
                    .Append(new SharedFramework(referencePath, implementationPath, referenceDlls, implementationDlls))
                    .ToArray()
            };
        }

        private static string GetCurrentAssemblyReferencePath(string framework, Version version)
        {
            var dotnetRuntimePath = RuntimeEnvironment.GetRuntimeDirectory();
            var dotnetRoot = Path.GetFullPath(Path.Combine(dotnetRuntimePath, "../../../packs/", framework + ".Ref"));
            var referenceAssemblyPath = Directory
                .GetDirectories(dotnetRoot, "net*" + version.Major + "." + version.Minor + "*", SearchOption.AllDirectories)
                .Last();
            return Path.GetFullPath(referenceAssemblyPath);
        }

        private static string GetCurrentAssemblyImplementationPath(string framework, Version version)
        {
            var currentFrameworkPath = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(typeof(object).Assembly.Location), ".."));
            var configuredFramework = currentFrameworkPath.Replace(SharedFramework.NetCoreApp, framework);
            var configuredFrameworkAndVersion = Directory
                .GetDirectories(configuredFramework, version.Major + "*")
                .OrderBy(path =>
                {
                    var versionString = Path.GetFileName(path).Split('-', 2).First(); // discard trailing preview versions, e.g. 6.0.0-preview.4.21253.7 
                    return new Version(versionString);
                })
                .Last();

            return configuredFrameworkAndVersion;
        }

        private static (string framework, Version version) GetDesiredFrameworkVersion(string sharedFramework)
        {
            var parts = sharedFramework.Split('/');
            return parts.Length switch
            {
                1 => (parts[0], Environment.Version),
                2 => (parts[0], new Version(parts[1])),
                _ => throw new InvalidOperationException("Unknown Shared Framework configuration: " + sharedFramework)
            };
        }

        private IReadOnlyCollection<MetadataReference> CreateDefaultReferences(string assemblyPath, IReadOnlyCollection<string> assemblies)
        {
            return assemblies
                .AsParallel()
                .Select(dll =>
                {
                    var fullReferencePath = Path.Combine(assemblyPath, dll);
                    var fullDocumentationPath = Path.ChangeExtension(fullReferencePath, ".xml");

                    if (!IsManagedAssembly(fullReferencePath))
                        return null;
                    if (!File.Exists(fullDocumentationPath))
                        return MetadataReference.CreateFromFile(fullReferencePath);

                    return MetadataReference.CreateFromFile(fullReferencePath, documentation: XmlDocumentationProvider.CreateFromFile(fullDocumentationPath));
                })
                .Where(reference => reference is not null)
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

    public record SharedFramework(
        string ReferencePath, string ImplementationPath,
        IReadOnlyCollection<MetadataReference> ReferenceAssemblies, IReadOnlyCollection<MetadataReference> ImplementationAssemblies)
    {
        public const string NetCoreApp = "Microsoft.NETCore.App";

        public static IReadOnlyCollection<string> SupportedFrameworks { get; } =
            Directory
                .GetDirectories(
                    Path.Combine(Path.GetDirectoryName(typeof(object).Assembly.Location), "../../")
                )
                .Select(Path.GetFileName)
                .ToArray();
    }
}
