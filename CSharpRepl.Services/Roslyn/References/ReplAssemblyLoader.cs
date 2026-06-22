// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Loader;
using CSharpRepl.Services.Nuget;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Scripting.Hosting;
using Microsoft.Extensions.DependencyModel;

namespace CSharpRepl.Services.Roslyn.References;

/// <summary>
/// Owns runtime loading of the assemblies the REPL pulls in at runime:
/// - the nuget closures restored for #r "nuget:"
/// - the transitive dependencies of #r'd on-disk DLLs.
/// Everything loads into a single <see cref="AssemblyLoadContext"/> so a given assembly has exactly one runtime identity
/// no matter which path loads it (avoiding the type-identity split behind https://github.com/waf/CSharpRepl/issues/355).
/// </summary>
/// <remarks>
/// A <see cref="registry"/> maps each assembly's simple name to a single canonical path (highest version wins,
/// like NuGet restore), seeded eagerly at #r time from the restore closure (<see cref="RegisterPinned"/>)
/// and from each #r'd DLL's deps.json (<see cref="RegisterDepsClosure"/>).
/// 
/// Also contains <see cref="ResolveMissingAssembly"/> -- the assembly-resolve fallback. It first consults the
/// registry, then falls back to a highest-version scan of the implementation-assembly paths.
/// 
/// This reproduces the version roll-forward behavior a custom load context otherwise lacks.
///
/// Compile-time metadata resolution lives in <see cref="MetadataResolvers.AssemblyReferenceMetadataResolver"/>;
/// this type is the runtime half.
/// 
/// Elsewhere, the injected inspector engine has its own analogous loader+pinning (InspectorEngine.cs), but it
/// runs in the target process, and is separate.
/// </remarks>
internal sealed class ReplAssemblyLoader
{
    private readonly AssemblyReferenceService referenceAssemblyService;
    private readonly InteractiveAssemblyLoader interactiveAssemblyLoader;
    private readonly IConsoleService console;

    // The single load context for assemblies the REPL loads at run time. Keeping it to one context means a
    // given assembly has one runtime identity whether it's pinned here or loaded by the resolve fallback.
    private readonly AssemblyLoadContext loadContext = new(nameof(CSharpRepl) + "LoadContext");

    // simple name (case-insensitive) -> the single canonical assembly we resolve that name to. Highest version
    // wins, mirroring restore; for same-version RID variants the most-specific RID asset is chosen at seeding time.
    private readonly ConcurrentDictionary<string, Entry> registry = new(StringComparer.OrdinalIgnoreCase);

    public AssemblyLoadContext LoadContext => loadContext;

    public ReplAssemblyLoader(InteractiveAssemblyLoader interactiveAssemblyLoader, AssemblyReferenceService referenceAssemblyService, IConsoleService console)
    {
        this.interactiveAssemblyLoader = interactiveAssemblyLoader;
        this.referenceAssemblyService = referenceAssemblyService;
        this.console = console;

        // Resolve misses through the modern, ALC-correct mechanism rather than the legacy process-wide
        // AppDomain.AssemblyResolve hook. We attach to the Default context and Roslyn's submission context (the
        // latter is created later, hence the discover-as-they-appear hook), so a submission's transitive miss
        // falls through here. Unlike AssemblyResolve, Resolving carries no requesting assembly, which is fine
        // because the deps.json closure is now seeded eagerly into the registry (RegisterDepsClosure). #355 / #128
        new AssemblyLoadContextHook(context => context.Resolving += ResolveMissingAssembly).EnsureInstalled();
    }

    /// <summary>
    /// Records an assembly path under its simple name, keeping the highest version when a name is registered more
    /// than once (mirroring how NuGet restore unifies a closure to one version per assembly).
    /// </summary>
    public void Register(string path)
    {
        Version version;
        string? name;
        try
        {
            var identity = AssemblyName.GetAssemblyName(path);
            name = identity.Name;
            version = identity.Version ?? new Version(0, 0, 0, 0);
        }
        catch
        {
            return; // not a managed assembly (e.g. a native dll that slipped in) - nothing to register
        }

        if (name is null)
        {
            return;
        }

        registry.AddOrUpdate(
            name,
            _ => new Entry(path, version),
            (_, existing) => version > existing.Version ? new Entry(path, version) : existing);
    }

    /// <summary>
    /// Loads each resolved nuget-package assembly exactly once into the single load context and pins it with
    /// <see cref="InteractiveAssemblyLoader.RegisterDependency(System.Reflection.Assembly)"/>, so at runtime the
    /// submission and any transitive, possibly lower-version, dependency request (e.g. a package built against
    /// EF Core 8.0.2 bound to the unified 8.0.3) resolves to that one instance.
    /// </summary>
    /// <remarks>
    /// Without this the scripting loader and the assembly resolver each load their own copy, giving the same type two
    /// identities and a MissingMethodException. Restoring as one project (single version per assembly) is what makes
    /// pinning safe. Also records the assembly in the registry so the resolve fallback agrees with the pinned copy.
    /// https://github.com/waf/CSharpRepl/issues/355
    /// </remarks>
    public void RegisterPinned(IEnumerable<PortableExecutableReference> references)
    {
        foreach (var reference in references)
        {
            if (reference.FilePath is string path && File.Exists(path))
            {
                Register(path);
                try
                {
                    interactiveAssemblyLoader.RegisterDependency(loadContext.LoadFromAssemblyPath(path));
                }
                catch
                {
                    // Best effort: a native/ref-only image that can't be loaded, or an identity already pinned.
                }
            }
        }
    }

    /// <summary>
    /// Eagerly expands a #r'd DLL's deps.json into the registry, resolving each transitive dependency to the
    /// most RID-specific runtime file available (e.g. runtimes/win/lib/.../System.Management.dll rather than
    /// the RID-agnostic lib/ copy). Doing this at #r time - rather than lazily on an
    /// AssemblyResolve keyed on the requesting assembly - is what lets the run-time fallback be a plain
    /// name lookup. https://github.com/waf/CSharpRepl/issues/128
    /// </summary>
    public void RegisterDepsClosure(string dllPath, DependencyContext dependencyContext)
    {
        var baseDirectory = Path.GetDirectoryName(dllPath);
        if (baseDirectory is null)
        {
            return;
        }

        var runtimeGraph = NugetHelper.GetRuntimeGraph(error: null);
        var runtimeIdentifier = RuntimeInformation.RuntimeIdentifier;
        var ridExpansion = runtimeGraph.ExpandRuntime(runtimeIdentifier).ToList();

        foreach (var runtimeLib in dependencyContext.RuntimeLibraries)
        {
            // Pick the single most RID-specific compatible asset group, mirroring how the host's binder selects
            // one RID's assets. Same-version variants (lib/ vs runtimes/<rid>/lib/) tie on version, so specificity
            // - not version - is what disambiguates them. https://github.com/waf/CSharpRepl/issues/128
            RuntimeAssetGroup? best = null;
            var bestRank = int.MaxValue;
            foreach (var group in runtimeLib.RuntimeAssemblyGroups)
            {
                var runtime = group.Runtime ?? "";
                if (!runtimeGraph.AreCompatible(runtimeIdentifier, runtime))
                {
                    continue;
                }

                // Earlier in the RID expansion == more specific; the RID-agnostic group (empty runtime) is least specific.
                var rank = runtime.Length == 0 ? int.MaxValue - 1 : ridExpansion.IndexOf(runtime);
                if (rank < 0)
                {
                    rank = int.MaxValue - 1; // compatible but not enumerated, treat as a least-specific fallback
                }

                if (rank < bestRank)
                {
                    bestRank = rank;
                    best = group;
                }
            }

            if (best is null)
            {
                continue;
            }

            foreach (var runtimeFile in best.RuntimeFiles)
            {
                var relativePath = runtimeFile.Path.Replace('/', Path.DirectorySeparatorChar);
                var fullPath = Path.Combine(baseDirectory, relativePath);
                if (File.Exists(fullPath))
                {
                    Register(fullPath);
                }
            }
        }
    }

    /// <summary>
    /// The run-time resolve fallback (an <see cref="AssemblyLoadContext.Resolving"/> handler): bind a requested
    /// assembly name to the single instance the REPL has chosen for it - the registry entry (restore closure /
    /// RID-correct deps), or failing that the highest-version match among the implementation-assembly paths.
    /// Reproduces the default host's version roll-forward that a custom load context lacks.
    /// https://github.com/waf/CSharpRepl/issues/355
    /// </summary>
    private Assembly? ResolveMissingAssembly(AssemblyLoadContext context, AssemblyName assemblyName)
    {
        Assembly? located = null;
        if (assemblyName.Name is string simpleName)
        {
            if (registry.TryGetValue(simpleName, out var entry))
            {
                located = TryLoad(entry.Path);
            }

            located ??= ScanImplementationPathsForHighestVersion(simpleName);
        }

        if (located?.FullName is not null && new AssemblyName(located.FullName).Version != assemblyName.Version)
        {
            // Resolving carries no requesting assembly, so the old "Requested by" line is gone.
            console.WriteLine($"Warning: Missing assembly: {assemblyName.FullName}");
            console.WriteLine($"            Using instead: {located.FullName}");
        }

        return located;
    }

    private Assembly? ScanImplementationPathsForHighestVersion(string simpleName)
    {
        string? bestPath = null;
        Version? bestVersion = null;
        foreach (var directory in referenceAssemblyService.ImplementationAssemblyPaths)
        {
            var candidate = Path.Combine(directory, simpleName + ".dll");
            if (!File.Exists(candidate))
            {
                continue;
            }

            Version? version;
            try
            {
                version = AssemblyName.GetAssemblyName(candidate).Version;
            }
            catch
            {
                continue;
            }

            if (bestVersion is null || version > bestVersion)
            {
                bestVersion = version;
                bestPath = candidate;
            }
        }

        return bestPath is null ? null : TryLoad(bestPath);
    }

    private Assembly? TryLoad(string path)
    {
        try
        {
            return loadContext.LoadFromAssemblyPath(path);
        }
        catch
        {
            return null;
        }
    }

    private sealed record Entry(string Path, Version Version);
}
