using Microsoft.CodeAnalysis;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;

namespace Sharply.Services.Roslyn
{
    /// <remarks>
    /// Useful notes https://github.com/dotnet/roslyn/blob/main/docs/wiki/Runtime-code-generation-using-Roslyn-compilations-in-.NET-Core-App.md
    /// </remarks>
    class ReferenceAssemblyService
    {
        private readonly Dictionary<string, MetadataReference> CachedMetadataReferences;
        private readonly HashSet<MetadataReference> LoadedReferenceAssemblies;

        public IReadOnlyCollection<string> ReferenceAssemblyPaths { get; }
        public IReadOnlyCollection<string> ImplementationAssemblyPaths { get; }
        public IReadOnlyCollection<MetadataReference> DefaultImplementationAssemblies { get; }
        public IReadOnlyCollection<MetadataReference> DefaultReferenceAssemblies { get; }
        public IReadOnlyCollection<string> DefaultUsings { get; }

        public ReferenceAssemblyService(Configuration config)
        {
            this.CachedMetadataReferences = new Dictionary<string, MetadataReference>();
            this.LoadedReferenceAssemblies = new HashSet<MetadataReference>(new EqualityComparerFunc<MetadataReference>(
                (r1, r2) => r1.Display.Equals(r2.Display),
                (r1) => r1.Display.GetHashCode()
            ));

            var (framework, version) = GetDesiredFrameworkVersion(config.Framework);
            var sharedFrameworks = GetSharedFrameworkConfiguration(framework, version);

            this.ReferenceAssemblyPaths = sharedFrameworks.Select(framework => framework.ReferencePath).ToArray();
            this.ImplementationAssemblyPaths = sharedFrameworks.Select(framework => framework.ImplementationPath).ToArray();

            this.DefaultReferenceAssemblies = sharedFrameworks
                .SelectMany(sharedFramework => CreateDefaultReferences(
                    sharedFramework.ReferencePath,
                    sharedFramework.ReferenceAssemblies
                ))
                .ToList();

            this.DefaultImplementationAssemblies = sharedFrameworks
                .SelectMany(sharedFramework => CreateDefaultReferences(
                    sharedFramework.ImplementationPath,
                    sharedFramework.ImplementationAssemblies
                ))
                .Concat(CreateDefaultReferences("", config.References))
                .ToList();

            this.DefaultUsings = new[] {
                    "System", "System.IO", "System.Collections.Generic",
                    "System.Linq", "System.Net.Http",
                    "System.Text", "System.Threading.Tasks"
                }
                .Concat(config.Usings)
                .ToList();
        }

        internal IReadOnlyCollection<MetadataReference> EnsureReferenceAssemblyWithDocumentation(IReadOnlyCollection<MetadataReference> references)
        {
            LoadedReferenceAssemblies.UnionWith(
                references.Select(suppliedReference => EnsureReferenceAssembly(suppliedReference)).Where(reference => reference is not null)
            );
            return LoadedReferenceAssemblies;
        }

        // a bit of a mismatch -- the scripting APIs use implementation assemblies only. The general workspace/project roslyn APIs use the reference
        // assemblies. Just to ensure that we don't accidentally use an implementation assembly with the roslyn APIs, do some "best-effort" conversion
        // here. If it's a reference assembly, pass it through unchanged. If it's an implementation assembly, try to locate the corresponding reference assembly.
        private MetadataReference EnsureReferenceAssembly(MetadataReference reference)
        {
            string suppliedAssemblyPath = reference.Display;
            if (CachedMetadataReferences.TryGetValue(suppliedAssemblyPath, out MetadataReference cachedReference))
            {
                return cachedReference;
            }

            // it's already a reference assembly, just cache it and use it.
            if (ReferenceAssemblyPaths.Any(path => reference.Display.StartsWith(path)))
            {
                CachedMetadataReferences[suppliedAssemblyPath] = reference;
                return reference;
            }

            // it's probably an implementation assembly, find the corresponding reference assembly and documentation if we can.

            var suppliedAssemblyName = Path.GetFileName(suppliedAssemblyPath);
            var assembly = ReferenceAssemblyPaths
                .Select(path => Path.Combine(path, suppliedAssemblyName))
                .FirstOrDefault(potentialReferencePath => File.Exists(potentialReferencePath))
                ?? suppliedAssemblyPath;

            if (ImplementationAssemblyPaths.Any(path => assembly.StartsWith(path)))
            {
                return null;
            }

            var potentialDocumentationPath = Path.ChangeExtension(assembly, ".xml");
            var documentation = File.Exists(potentialDocumentationPath)
                ? XmlDocumentationProvider.CreateFromFile(potentialDocumentationPath)
                : null;

            var completeMetadataReference = MetadataReference.CreateFromFile(assembly, documentation: documentation);
            CachedMetadataReferences[suppliedAssemblyPath] = completeMetadataReference;
            return completeMetadataReference;
        }

        private SharedFramework[] GetSharedFrameworkConfiguration(string framework, string version)
        {
            var referencePath = GetCurrentAssemblyReferencePath(framework, version);
            var implementationPath = GetCurrentAssemblyImplementationPath(framework, version);

            var referenceDlls = Directory.GetFiles(referencePath, "*.dll", SearchOption.TopDirectoryOnly);
            var implementationDlls = Directory.GetFiles(implementationPath, "*.dll", SearchOption.TopDirectoryOnly);

            // Microsoft.NETCore.App is always included.
            // If we're including e.g. Microsoft.AspNetCore.App, include it alongside Microsoft.NETCore.App.
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

        private static string GetCurrentAssemblyReferencePath(string framework, string version)
        {
            var dotnetRuntimePath = RuntimeEnvironment.GetRuntimeDirectory();
            var dotnetRoot = Path.GetFullPath(Path.Combine(dotnetRuntimePath, "../../../packs/", framework + ".Ref"));
            var referenceAssemblyPath = Directory
                .GetDirectories(dotnetRoot, "net*" + version + "*", SearchOption.AllDirectories)
                .Last();
            return Path.GetFullPath(referenceAssemblyPath);
        }

        private static string GetCurrentAssemblyImplementationPath(string framework, string version)
        {
            var currentFrameworkPath = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(typeof(object).Assembly.Location), ".."));
            var configuredFramework = currentFrameworkPath.Replace(SharedFramework.NetCoreApp, framework);
            var configuredFrameworkAndVersion = Directory
                .GetDirectories(configuredFramework, version + "*")
                .OrderBy(path => new Version(Path.GetFileName(path)))
                .Last();

            return configuredFrameworkAndVersion;
        }

        private static (string framework, string version) GetDesiredFrameworkVersion(string sharedFramework)
        {
            var parts = sharedFramework.Split('/');
            if (parts.Length == 2)
            {
                return (parts[0], parts[1]);
            }
            else if (parts.Length == 1)
            {
                return (parts[0], Environment.Version.Major.ToString());
            }
            else
            {
                throw new InvalidOperationException("Unknown Shared Framework configuration: " + sharedFramework);
            }
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

    public record SharedFramework(string ReferencePath, string ImplementationPath, string[] ReferenceAssemblies, string[] ImplementationAssemblies)
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
