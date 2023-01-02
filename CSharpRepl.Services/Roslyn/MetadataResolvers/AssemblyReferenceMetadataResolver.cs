// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Loader;
using System.Text.Json;
using CSharpRepl.Services.Nuget;
using CSharpRepl.Services.Roslyn.References;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Scripting;
using Microsoft.Extensions.DependencyModel;
using Spectre.Console;

namespace CSharpRepl.Services.Roslyn.MetadataResolvers;

/// <summary>
/// Resolves absolute and relative assembly references. If the assembly has an adjacent
/// assembly.runtimeconfig.json file, the file will be read in order to determine required
/// Shared Frameworks. https://natemcmaster.com/blog/2018/08/29/netcore-primitives-2/
/// </summary>
internal sealed class AssemblyReferenceMetadataResolver : IIndividualMetadataReferenceResolver
{
    private readonly AssemblyReferenceService referenceAssemblyService;
    private readonly AssemblyLoadContext loadContext;
    private readonly DependencyContextJsonReader dependencyContextJsonReader = new();
    private readonly IConsoleEx console;
    private readonly Dictionary<string, DependenciesInfo> dependencyContextsPerAssemblyName = new();

    public AssemblyReferenceMetadataResolver(IConsoleEx console, AssemblyReferenceService referenceAssemblyService)
    {
        this.console = console;
        this.referenceAssemblyService = referenceAssemblyService;
        this.loadContext = new AssemblyLoadContext(nameof(CSharpRepl) + "LoadContext");

        AppDomain.CurrentDomain.AssemblyResolve += new ResolveEventHandler(ResolveByAssemblyName);
    }

    public ImmutableArray<PortableExecutableReference> ResolveReference(
        string reference, string? baseFilePath, MetadataReferenceProperties properties, MetadataReferenceResolver compositeResolver)
    {
        // resolve relative filepaths
        var fileSystemPath = Path.GetFullPath(reference);
        if (File.Exists(fileSystemPath))
        {
            reference = fileSystemPath;
        }

        var references = ScriptMetadataResolver.Default.WithSearchPaths(referenceAssemblyService.ImplementationAssemblyPaths).ResolveReference(reference, baseFilePath, properties);

        foreach (var loadedReference in references)
        {
            if (TryGetDepsJsonPath(loadedReference.FilePath, out var runtimeConfigJsonPath))
            {
                var path = loadedReference.FilePath!;
                using var fs = File.OpenRead(runtimeConfigJsonPath);
                var cfg = dependencyContextJsonReader.Read(fs);
                dependencyContextsPerAssemblyName.Add(AssemblyLoadContext.GetAssemblyName(path).FullName, new(cfg, path));
            }
            LoadSharedFramework(loadedReference);
        }

        return references;
    }

    private void LoadSharedFramework(PortableExecutableReference reference)
    {
        if (TryGetRuntimeConfigJsonPath(reference.FilePath, out var configPath))
        {
            var content = File.ReadAllText(configPath);
            if (JsonSerializer.Deserialize<RuntimeConfigJson>(content, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }) is RuntimeConfigJson config)
            {
                var framework = config.RuntimeOptions.Framework;
                if (framework != null) LoadFramework(framework);

                var frameworks = config.RuntimeOptions.Frameworks;
                if (frameworks != null)
                {
                    foreach (var fw in frameworks)
                    {
                        LoadFramework(fw);
                    }
                }

                void LoadFramework(RuntimeConfigJson.FrameworkKey framework)
                {
                    var name = framework.Name;
                    var version = SharedFramework.ToDotNetVersion(framework.Version);
                    referenceAssemblyService.LoadSharedFrameworkConfiguration(name, version);
                }
            }
        }
    }

    private static bool TryGetRuntimeConfigJsonPath(string? assemblyPath, [NotNullWhen(true)] out string? runtimeConfigJsonPath)
        => TryGetAssemblyCfgPath(assemblyPath, "runtimeconfig.json", out runtimeConfigJsonPath);

    private static bool TryGetDepsJsonPath(string? assemblyPath, [NotNullWhen(true)] out string? depsConfigJsonPath)
        => TryGetAssemblyCfgPath(assemblyPath, "deps.json", out depsConfigJsonPath);

    private static bool TryGetAssemblyCfgPath(string? assemblyPath, string extension, [NotNullWhen(true)] out string? runtimeConfigJsonPath)
    {
        runtimeConfigJsonPath = Path.ChangeExtension(assemblyPath, extension);
        if (runtimeConfigJsonPath is null || !File.Exists(runtimeConfigJsonPath))
        {
            runtimeConfigJsonPath = null;
            return false;
        }
        return true;
    }

    /// <summary>
    /// If we're missing an assembly (by exact match), try to find an assembly with the same name but different version.
    /// </summary>
    private Assembly? ResolveByAssemblyName(object? sender, ResolveEventArgs args)
    {
        var assemblyName = new AssemblyName(args.Name);
        Assembly? located = null;

        if (args.RequestingAssembly?.FullName != null &&
            dependencyContextsPerAssemblyName.TryGetValue(args.RequestingAssembly.FullName, out var depsInfo))
        {
            var runtimeGraph = NugetHelper.GetRuntimeGraph(error: null);
            foreach (var runtimeLib in depsInfo.DependencyContext.RuntimeLibraries)
            {
                if (runtimeLib.Name == assemblyName.Name)
                {
                    foreach (var assemblyGroup in runtimeLib.RuntimeAssemblyGroups)
                    {
                        if (runtimeGraph.AreCompatible(RuntimeInformation.RuntimeIdentifier, assemblyGroup.Runtime))
                        {
                            foreach (var runtimeFile in assemblyGroup.RuntimeFiles)
                            {
                                if (Path.GetFileNameWithoutExtension(runtimeFile.Path) == assemblyName.Name)
                                {
                                    var path = Path.Combine(Path.GetDirectoryName(depsInfo.AssemblyPath)!, runtimeFile.Path);
                                    located = loadContext.LoadFromAssemblyPath(path);
                                }
                            }
                        }
                    }
                }
            }
        }

        located ??= referenceAssemblyService.ImplementationAssemblyPaths
                .SelectMany(path => Directory.GetFiles(path, "*.dll"))
                .Where(file => Path.GetFileNameWithoutExtension(file) == assemblyName.Name)
                .Select(loadContext.LoadFromAssemblyPath)
                .FirstOrDefault();

        if (located?.FullName is not null && new AssemblyName(located.FullName).Version != assemblyName.Version)
        {
            console.WriteLine($"Warning: Missing assembly: {args.Name}");
            console.WriteLine($"            Using instead: {located.FullName}");
            if (args.RequestingAssembly is not null)
            {
                console.WriteLine($"             Requested by: {args.RequestingAssembly.FullName}");
            }
        }

        return located;
    }

    private readonly struct DependenciesInfo
    {
        public readonly DependencyContext DependencyContext;

        /// <summary>
        /// This holds original assembly location. When '#load "x.dll"' is executed the script engine copies dll to temp dir and loads it from there.
        /// </summary>
        public readonly string AssemblyPath;

        public DependenciesInfo(DependencyContext dependencyContext, string assemblyPath)
        {
            DependencyContext = dependencyContext;
            AssemblyPath = assemblyPath;
        }
    }
}

/// <summary>
/// Schema for assembly.runtimeconfig.json files.
/// </summary>
internal sealed class RuntimeConfigJson
{
    public RuntimeOptionsKey RuntimeOptions { get; }

    public RuntimeConfigJson(RuntimeOptionsKey runtimeOptions)
    {
        this.RuntimeOptions = runtimeOptions;
    }

    public sealed class RuntimeOptionsKey
    {
        public RuntimeOptionsKey(FrameworkKey framework, FrameworkKey[] frameworks)
        {
            this.Framework = framework;
            this.Frameworks = frameworks;
        }

        public FrameworkKey? Framework { get; }
        public FrameworkKey[]? Frameworks { get; }
    }

    public sealed class FrameworkKey
    {
        public FrameworkKey(string name, string version)
        {
            this.Name = name;
            this.Version = version;
        }

        public string Name { get; }
        public string Version { get; }
    }
}
