// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using CSharpRepl.Services.Extensions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;

namespace CSharpRepl.Services.Roslyn.References
{
    /// <summary>
    /// Manages references to assemblies. It tracks "reference assemblies" and "implementation assemblies" separately,
    /// because the Script APIs require implementation assemblies and the workspace APIs require reference assemblies.
    /// This service is stateful, as assemblies and shared frameworks can be added dynamically in the REPL.
    /// </summary>
    /// <remarks>
    /// Useful notes https://github.com/dotnet/roslyn/blob/main/docs/wiki/Runtime-code-generation-using-Roslyn-compilations-in-.NET-Core-App.md
    /// </remarks>
    internal sealed class AssemblyReferenceService
    {
        private readonly ConcurrentDictionary<string, MetadataReference> cachedMetadataReferences;
        private readonly HashSet<MetadataReference> loadedReferenceAssemblies;
        private readonly HashSet<MetadataReference> loadedImplementationAssemblies;
        private readonly HashSet<string> referenceAssemblyPaths;
        private readonly HashSet<string> implementationAssemblyPaths;
        private readonly HashSet<string> sharedFrameworkImplementationAssemblyPaths;
        private readonly HashSet<UsingDirectiveSyntax> usings;

        public IReadOnlySet<string> ImplementationAssemblyPaths => implementationAssemblyPaths;
        public IReadOnlySet<MetadataReference> LoadedImplementationAssemblies => loadedImplementationAssemblies;
        public IReadOnlySet<MetadataReference> LoadedReferenceAssemblies => loadedReferenceAssemblies;
        public IReadOnlyCollection<UsingDirectiveSyntax> Usings => usings;

        public AssemblyReferenceService(Configuration config)
        {
            this.referenceAssemblyPaths = new();
            this.implementationAssemblyPaths = new();
            this.sharedFrameworkImplementationAssemblyPaths = new();
            this.cachedMetadataReferences = new();
            this.loadedReferenceAssemblies = new(new AssemblyReferenceComparer());
            this.loadedImplementationAssemblies = new(new AssemblyReferenceComparer());

            this.usings = new[] {
                    "System", "System.IO", "System.Collections.Generic",
                    "System.Linq", "System.Net.Http",
                    "System.Text", "System.Threading.Tasks"
                }
                .Concat(config.Usings)
                .Select(name => SyntaxFactory.UsingDirective(SyntaxFactory.ParseName(name)))
                .ToHashSet();

            var (framework, version) = GetDesiredFrameworkVersion(config.Framework);
            var sharedFrameworks = GetSharedFrameworkConfiguration(framework, version);
            LoadSharedFrameworkConfiguration(sharedFrameworks);
        }

        internal IReadOnlyCollection<MetadataReference> EnsureReferenceAssemblyWithDocumentation(IReadOnlyCollection<MetadataReference> references)
        {
            loadedReferenceAssemblies.UnionWith(
                references.Select(suppliedReference => EnsureReferenceAssembly(suppliedReference)).WhereNotNull()
            );
            return loadedReferenceAssemblies;
        }

        /// <summary>
        /// Resolves a bit of a mismatch: the scripting APIs use implementation assemblies only. The general workspace/project roslyn APIs use the reference
        /// assemblies. We don't want to accidentally use an implementation assembly with the workspace APIs, so do some "best-effort" conversion
        /// here. If it's a reference assembly, pass it through unchanged. If it's an implementation assembly, try to locate the corresponding reference assembly.
        /// </summary>
        /// <remarks>This method can run in multiple threads due to the "main" thread and the "background initialization" thread.</remarks>
        private MetadataReference? EnsureReferenceAssembly(MetadataReference reference)
        {
            string? suppliedAssemblyPath = reference.Display;

            if (suppliedAssemblyPath is null) // if we don't have an assembly path or display, we won't make any decision because we have nothing to go on.
            {
                return reference;
            }

            if (cachedMetadataReferences.TryGetValue(suppliedAssemblyPath, out MetadataReference? cachedReference))
            {
                return cachedReference;
            }

            // it's already a reference assembly, just cache it and use it.
            if (referenceAssemblyPaths.Any(path => suppliedAssemblyPath.StartsWith(path)))
            {
                cachedMetadataReferences[suppliedAssemblyPath] = reference;
                return reference;
            }

            // it's probably an implementation assembly, find the corresponding reference assembly and documentation if we can.

            var suppliedAssemblyFileName = Path.GetFileName(suppliedAssemblyPath);
            var suppliedAssemblyName = AssemblyName.GetAssemblyName(suppliedAssemblyPath).ToString();
            var assembly = referenceAssemblyPaths
                .Select(path => Path.Combine(path, suppliedAssemblyFileName))
                .FirstOrDefault(potentialReferencePath => File.Exists(potentialReferencePath) && AssemblyName.GetAssemblyName(potentialReferencePath).ToString() == suppliedAssemblyName)
                ?? suppliedAssemblyPath;

            if (sharedFrameworkImplementationAssemblyPaths.Any(path => assembly.StartsWith(path)))
            {
                return null;
            }

            var potentialDocumentationPath = Path.ChangeExtension(assembly, ".xml");
            var documentation = File.Exists(potentialDocumentationPath)
                ? XmlDocumentationProvider.CreateFromFile(potentialDocumentationPath)
                : null;

            var completeMetadataReference = MetadataReference.CreateFromFile(assembly, documentation: documentation);
            cachedMetadataReferences[suppliedAssemblyPath] = completeMetadataReference;
            return completeMetadataReference;
        }

        internal void AddImplementationAssemblyReferences(IEnumerable<MetadataReference> references)
        {
            var paths = references
                .Select(r => Path.GetDirectoryName(r.Display) ?? r.Display) // GetDirectoryName returns null when at root directory
                .WhereNotNull();

            this.implementationAssemblyPaths.UnionWith(paths);
            this.loadedImplementationAssemblies.UnionWith(references);
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
            this.sharedFrameworkImplementationAssemblyPaths.UnionWith(sharedFrameworks.Select(framework => framework.ImplementationPath));
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
                    new SharedFramework(referencePath, referenceDlls, implementationPath, implementationDlls)
                },
                _ => GetSharedFrameworkConfiguration(SharedFramework.NetCoreApp, version)
                    .Append(new SharedFramework(referencePath, referenceDlls, implementationPath, implementationDlls))
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
            var currentFrameworkPath = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(typeof(object).Assembly.Location)!, ".."));
            var configuredFramework = currentFrameworkPath.Replace(SharedFramework.NetCoreApp, framework);
            var configuredFrameworkAndVersion = Directory
                .GetDirectories(configuredFramework, version.Major + "." + version.Minor + "*")
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

        internal IReadOnlyCollection<UsingDirectiveSyntax> GetUsings(string code) =>
            CSharpSyntaxTree.ParseText(code)
                .GetRoot()
                .DescendantNodes()
                .OfType<UsingDirectiveSyntax>()
                .ToList();

        internal void TrackUsings(IReadOnlyCollection<UsingDirectiveSyntax> usingsToAdd) =>
            usings.UnionWith(usingsToAdd);
    }

    public class SharedFramework
    {
        public const string NetCoreApp = "Microsoft.NETCore.App";
        public string ReferencePath { get; }
        public string ImplementationPath { get; }
        public IReadOnlyCollection<MetadataReference> ReferenceAssemblies { get; }
        public IReadOnlyCollection<MetadataReference> ImplementationAssemblies { get; }

        public SharedFramework(
            string referencePath, IReadOnlyCollection<MetadataReference> ReferenceAssemblies,
            string ImplementationPath, IReadOnlyCollection<MetadataReference> ImplementationAssemblies)
        {
            this.ReferencePath = referencePath;
            this.ImplementationPath = ImplementationPath;
            this.ReferenceAssemblies = ReferenceAssemblies;
            this.ImplementationAssemblies = ImplementationAssemblies;
        }

        public static IReadOnlyCollection<string> SupportedFrameworks { get; } =
            Path.GetDirectoryName(typeof(object).Assembly.Location) is string frameworkDirectory
            ? Directory
                .GetDirectories(Path.Combine(frameworkDirectory, "../../"))
                .Select(dir => Path.GetFileName(dir))
                .ToArray()
            : Array.Empty<string>();
    }
}
