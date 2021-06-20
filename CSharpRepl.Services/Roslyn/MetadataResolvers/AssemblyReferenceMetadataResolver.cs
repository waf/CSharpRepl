using CSharpRepl.Services.Nuget;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Scripting;
using PrettyPrompt.Consoles;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace CSharpRepl.Services.Roslyn.MetadataResolvers
{
    class AssemblyReferenceMetadataResolver : IChildMetadataReferenceResolver
    {
        private readonly ReferenceAssemblyService referenceAssemblyService;
        private readonly AssemblyLoadContext loadContext;
        private readonly IConsole console;

        public AssemblyReferenceMetadataResolver(IConsole console, ReferenceAssemblyService referenceAssemblyService)
        {
            this.console = console;
            this.referenceAssemblyService = referenceAssemblyService;
            this.loadContext = new AssemblyLoadContext(nameof(CSharpRepl) + "LoadContext");

            AppDomain.CurrentDomain.AssemblyResolve += new ResolveEventHandler(ChooseBestMatch);
        }

        public ImmutableArray<PortableExecutableReference> ResolveReference(
            string reference, string baseFilePath, MetadataReferenceProperties properties, MetadataReferenceResolver rootResolver)
        {
            // resolve relative filepaths
            var fileSystemPath = Path.GetFullPath(reference);
            if (File.Exists(fileSystemPath))
            {
                reference = fileSystemPath;
            }

            var references = ScriptMetadataResolver.Default.WithSearchPaths(referenceAssemblyService.ImplementationAssemblyPaths).ResolveReference(reference, baseFilePath, properties);
            LoadSharedFramework(references);
            return references;
        }

        private void LoadSharedFramework(ImmutableArray<PortableExecutableReference> references)
        {
            if (references.SingleOrDefault() is PortableExecutableReference resolvedReference)
            {
                var configPath = Path.ChangeExtension(resolvedReference.FilePath, "runtimeconfig.json");
                if (File.Exists(configPath))
                {
                    var content = File.ReadAllText(configPath);
                    var config = JsonSerializer.Deserialize<RuntimeConfigJson>(content);
                    var name = config.runtimeOptions.framework.name;
                    var version = new Version(config.runtimeOptions.framework.version);
                    referenceAssemblyService.LoadSharedFrameworkConfiguration(name, version);
                }
            }
        }

        /// <summary>
        /// If we're missing an assembly (by exact match), try to find an assembly with the same name but different version.
        /// </summary>
        private Assembly ChooseBestMatch(object sender, ResolveEventArgs args)
        {
            var assemblyName = new AssemblyName(args.Name);

            var located = referenceAssemblyService.ImplementationAssemblyPaths
                .SelectMany(path => Directory.GetFiles(path, "*.dll"))
                .Where(file => Path.GetFileNameWithoutExtension(file) == assemblyName.Name)
                .Select(loadContext.LoadFromAssemblyPath)
                .FirstOrDefault();
            
            if (located is not null && new AssemblyName(located.FullName).Version != assemblyName.Version)
            {
                console.WriteLine($"Warning: Missing assembly: {args.Name}");
                console.WriteLine($"            Using instead: {located.FullName}");
                console.WriteLine($"             Requested by: {args.RequestingAssembly.FullName}");
            }

            return located;
        }
    }
}
