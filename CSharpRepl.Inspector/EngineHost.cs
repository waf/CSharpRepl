// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.IO;
using System.Reflection;
using System.Runtime.Loader;
using CSharpRepl.Inspector.Contracts;

namespace CSharpRepl.Inspector;

/// <summary>
/// Loads the engine into an isolated <see cref="EngineLoadContext"/> and returns it as an
/// <see cref="IInspectorEngine"/>. No Roslyn type is touched here — the engine assembly and its Roslyn
/// closure load entirely inside the isolated ALC.
/// </summary>
internal static class EngineHost
{
    private const string EngineAssemblyName = "CSharpRepl.Inspector.Engine";
    private const string EngineTypeName = "CSharpRepl.Inspector.Engine.InspectorEngine";

    public static IInspectorEngine Load(string bootstrapDirectory)
    {
        var engineDll = Path.Combine(bootstrapDirectory, EngineAssemblyName + ".dll");
        if (!File.Exists(engineDll))
            throw new FileNotFoundException($"Inspector engine assembly not found next to the bootstrap.", engineDll);

        var context = new EngineLoadContext(engineDll);
        var engineAssembly = context.LoadFromAssemblyPath(engineDll);
        var engineType = engineAssembly.GetType(EngineTypeName)
            ?? throw new InvalidOperationException($"{EngineTypeName} not found in {engineDll}.");

        return (IInspectorEngine)Activator.CreateInstance(engineType)!;
    }
}

/// <summary>
/// Isolated ALC for the engine + its Roslyn closure. Resolves the engine's own dependencies (Roslyn) from
/// the bootstrap directory via <see cref="AssemblyDependencyResolver"/>, but delegates the shared contracts
/// assembly back to the default ALC (returning null) so its types stay identical across the boundary. The
/// shared framework is resolved by the runtime's default fallback.
/// </summary>
internal sealed class EngineLoadContext : AssemblyLoadContext
{
    private const string ContractsAssemblyName = "CSharpRepl.Inspector.Contracts";

    private readonly AssemblyDependencyResolver resolver;

    public EngineLoadContext(string engineMainDll)
        : base(name: "CSharpRepl.Inspector.Engine", isCollectible: false)
        => resolver = new AssemblyDependencyResolver(engineMainDll);

    protected override Assembly? Load(AssemblyName assemblyName)
    {
        // Share contracts with the default ALC (resolved there via the bootstrap's Resolving handler), so
        // IInspectorEngine / InspectorGlobals / RemoteValue / wire DTOs are type-identical across the boundary.
        if (assemblyName.Name == ContractsAssemblyName)
            return null;

        var path = resolver.ResolveAssemblyToPath(assemblyName);
        return path is not null ? LoadFromAssemblyPath(path) : null;
    }
}
