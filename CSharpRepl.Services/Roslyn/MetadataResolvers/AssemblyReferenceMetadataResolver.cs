// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Text.Json;
using CSharpRepl.Services.Roslyn.References;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Scripting;
using Microsoft.Extensions.DependencyModel;

namespace CSharpRepl.Services.Roslyn.MetadataResolvers;

/// <summary>
/// Resolves absolute and relative assembly references at compile time (path -> <see cref="PortableExecutableReference"/>).
/// As a side of resolution it reads two adjacent files when present: <c>*.runtimeconfig.json</c>, to pull in any
/// required Shared Frameworks (https://natemcmaster.com/blog/2018/08/29/netcore-primitives-2/), and <c>*.deps.json</c>,
/// which it hands to <see cref="ReplAssemblyLoader.RegisterDepsClosure"/> so the DLL's transitive dependencies can be
/// resolved to the correct RID-specific runtime file when the script runs.
/// </summary>
internal sealed class AssemblyReferenceMetadataResolver(AssemblyReferenceService referenceAssemblyService, ReplAssemblyLoader assemblyLoader) : IIndividualMetadataReferenceResolver
{
    private readonly DependencyContextJsonReader dependencyContextJsonReader = new();

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
            if (TryGetDepsJsonPath(loadedReference.FilePath, out var depsJsonPath))
            {
                using var fs = File.OpenRead(depsJsonPath);
                var dependencyContext = dependencyContextJsonReader.Read(fs);
                assemblyLoader.RegisterDepsClosure(loadedReference.FilePath!, dependencyContext);
            }
            LoadSharedFramework(loadedReference);
        }

        return references;
    }

    /// <summary>
    /// Resolves a transitive reference (an assembly that a #r'd DLL depends on but that wasn't itself
    /// #r'd) so its types bind at compile time. https://github.com/waf/CSharpRepl/issues/184.
    /// The matching run-time load is handled independently by <see cref="ReplAssemblyLoader"/>, which keys off the same search paths.
    /// </summary>
    public PortableExecutableReference? ResolveMissingAssembly(MetadataReference definition, AssemblyIdentity referenceIdentity)
        => ScriptMetadataResolver.Default
            .WithSearchPaths(referenceAssemblyService.ImplementationAssemblyPaths)
            .ResolveMissingAssembly(definition, referenceIdentity);

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
