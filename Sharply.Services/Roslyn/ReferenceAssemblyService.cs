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
        private readonly IDictionary<string, MetadataReference> CachedMetadataReferences = new Dictionary<string, MetadataReference>();
        private readonly HashSet<MetadataReference> UniqueMetadataReferences = new HashSet<MetadataReference>(new EqualityComparerFunc<MetadataReference>(
            (r1, r2) => r1.Display.Equals(r2.Display),
            (r1) => r1.Display.GetHashCode()
        ));

        public IReadOnlyCollection<string> ReferenceAssemblyPaths { get; }
        public IReadOnlyCollection<string> ImplementationAssemblyPaths { get; }
        public IReadOnlyCollection<MetadataReference> DefaultImplementationAssemblies { get; }
        public IReadOnlyCollection<MetadataReference> DefaultReferenceAssemblies { get; }
        public IReadOnlyCollection<string> DefaultUsings { get; }

        public ReferenceAssemblyService(Configuration config)
        {
            var sharedFrameworks = GetSharedFrameworkConfiguration(config.Framework);

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

            AppDomain.CurrentDomain.AssemblyResolve += new ResolveEventHandler(ChooseBestMatch);
        }

        /// <summary>
        /// If we're missing an assembly (by exact match), try to find an assembly with the same name but different version.
        /// </summary>
        private Assembly ChooseBestMatch(object sender, ResolveEventArgs args)
        {
            var assemblyName = new AssemblyName(args.Name).Name;
            var located = this.DefaultImplementationAssemblies
                .Where(a => Path.GetFileNameWithoutExtension(a.Display) == assemblyName)
                .Select(a => Assembly.LoadFile(a.Display))
                .FirstOrDefault();
            if (located is not null)
            {
                Console.WriteLine($@"Warning: Missing assembly: {args.Name}");
                Console.WriteLine($@"            Using instead: {located.FullName}");
                Console.WriteLine($@"             Requested by: {args.RequestingAssembly.FullName}");
            }
            return located;
        }

        private static string GetCurrentAssemblyImplementationPath(string sharedFramework)
        {
            return Path
                .GetDirectoryName(typeof(object).Assembly.Location)
                .Replace(SharedFramework.NetCoreApp, sharedFramework);
        }

        internal IReadOnlyCollection<MetadataReference> EnsureReferenceAssemblyWithDocumentation(IReadOnlyCollection<MetadataReference> references)
        {
            UniqueMetadataReferences.UnionWith(
                references.Select(suppliedReference => EnsureReferenceAssembly(suppliedReference)).Where(reference => reference is not null)
            );
            return UniqueMetadataReferences;
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

        private SharedFramework[] GetSharedFrameworkConfiguration(string sharedFramework)
        {
            var referencePath = GetCurrentAssemblyReferencePath(sharedFramework);
            var implementationPath = GetCurrentAssemblyImplementationPath(sharedFramework);

            var referenceDlls = Directory.GetFiles(referencePath, "*.dll", SearchOption.TopDirectoryOnly);
            var implementationDlls = Directory.GetFiles(implementationPath, "*.dll", SearchOption.TopDirectoryOnly);

            // NetCore.App is always included.
            // If we're including e.g. AspNetCore.App, include it alongside NetCore.App.
            return sharedFramework switch
            {
                SharedFramework.NetCoreApp => new[] {
                    new SharedFramework(sharedFramework, referencePath, implementationPath, referenceDlls, implementationDlls)
                },
                _ => GetSharedFrameworkConfiguration(SharedFramework.NetCoreApp)
                    .Append(new SharedFramework(sharedFramework, referencePath, implementationPath, referenceDlls, implementationDlls))
                    .ToArray()
            };
        }

        private static string GetCurrentAssemblyReferencePath(string sharedFramework)
        {
            var version = Environment.Version;
            var dotnetRuntimePath = RuntimeEnvironment.GetRuntimeDirectory();
            var dotnetRoot = Path.Combine(dotnetRuntimePath, "../../../packs/", sharedFramework + ".Ref");
            var referenceAssemblyPath = Directory
                .GetDirectories(dotnetRoot, "*net" + version.Major.ToString() + "*", SearchOption.AllDirectories)
                .Last();
            return Path.GetFullPath(referenceAssemblyPath);
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

    public record SharedFramework(string Name, string ReferencePath, string ImplementationPath, string[] ReferenceAssemblies, string[] ImplementationAssemblies)
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
