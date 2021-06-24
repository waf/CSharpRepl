// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Scripting;
using PrettyPrompt.Consoles;
using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json;

namespace CSharpRepl.Services.Roslyn.MetadataResolvers
{
    /// <summary>
    /// Resolves absolute and relative assembly references. If the assembly has an adjacent
    /// assembly.runtimeconfig.json file, the file will be read in order to determine required
    /// Shared Frameworks. https://natemcmaster.com/blog/2018/08/29/netcore-primitives-2/
    /// </summary>
    class AssemblyReferenceMetadataResolver : IIndividualMetadataReferenceResolver
    {
        private readonly ReferenceAssemblyService referenceAssemblyService;
        private readonly AssemblyLoadContext loadContext;
        private readonly IConsole console;

        public AssemblyReferenceMetadataResolver(IConsole console, ReferenceAssemblyService referenceAssemblyService)
        {
            this.console = console;
            this.referenceAssemblyService = referenceAssemblyService;
            this.loadContext = new AssemblyLoadContext(nameof(CSharpRepl) + "LoadContext");

            AppDomain.CurrentDomain.AssemblyResolve += new ResolveEventHandler(ResolveByAssemblyName);
        }

        public ImmutableArray<PortableExecutableReference> ResolveReference(
            string reference, string baseFilePath, MetadataReferenceProperties properties, MetadataReferenceResolver compositeResolver)
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
            if (references.Length != 1) return;

            var configPath = Path.ChangeExtension(references[0].FilePath, "runtimeconfig.json");

            if (!File.Exists(configPath))
            {
                return;
            }

            var content = File.ReadAllText(configPath);
            var config = JsonSerializer.Deserialize<RuntimeConfigJson>(content);
            var name = config.runtimeOptions.framework.name;
            var version = new Version(config.runtimeOptions.framework.version);
            referenceAssemblyService.LoadSharedFrameworkConfiguration(name, version);
        }

        /// <summary>
        /// If we're missing an assembly (by exact match), try to find an assembly with the same name but different version.
        /// </summary>
        private Assembly ResolveByAssemblyName(object sender, ResolveEventArgs args)
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

    /// <summary>
    /// Schema for assembly.runtimeconfig.json files.
    /// </summary>
    public class RuntimeConfigJson
    {
        public RuntimeOptions runtimeOptions { get; set; }

        public class RuntimeOptions
        {
            public string tfm { get; set; }
            public Framework framework { get; set; }
        }

        public class Framework
        {
            public string name { get; set; }
            public string version { get; set; }
        }
    }
}
