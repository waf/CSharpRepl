// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Spike.Contracts;

/// <summary>
/// Static holder for the captured "roots" of the target. In the real design this would
/// hold the captured IServiceProvider; here it just holds a live object the Host created.
/// Lives in the DEFAULT ALC (shared) so both the Host and the engine see the same statics.
/// </summary>
public static class InspectorRoots
{
    public static object? Service;
}

/// <summary>
/// The globals object handed to the Roslyn submission chain. Submissions can reference
/// <see cref="Service"/> directly (it becomes an in-scope member, exactly like the local REPL's ScriptGlobals).
/// </summary>
public sealed class InspectorGlobals
{
    public object? Service => InspectorRoots.Service;
}

/// <summary>
/// The cross-ALC engine contract. The Host obtains this from the EngineALC and drives it.
/// Successfully casting the engine instance to this interface is itself proof that the
/// contracts assembly is type-identical across the boundary (otherwise: InvalidCastException).
/// </summary>
public interface IInspectorEngine
{
    /// <param name="registerLiveDependencies">
    /// When true, the engine calls InteractiveAssemblyLoader.RegisterDependency(...) for each
    /// live default-ALC assembly. When false, it does not — so we can measure whether
    /// RegisterDependency is actually load-bearing for cross-ALC live binding.
    /// </param>
    void Initialize(bool registerLiveDependencies);

    Task<EvalResult> EvalAsync(string code);

    /// <summary>typeof(InspectorGlobals) as the ENGINE sees it. The Host ReferenceEquals-compares
    /// this against its own typeof(InspectorGlobals) to prove single-load type identity.</summary>
    object GlobalsType { get; }

    /// <summary>Assembly version of the Roslyn the ENGINE uses. The clash scenario asserts this
    /// differs from the target's own Roslyn version.</summary>
    string RoslynVersion { get; }

    /// <summary>Name of the ALC the engine's Roslyn loaded into (expected: "EngineALC").</summary>
    string RoslynAlc { get; }
}

/// <summary>Serializable-ish result crossing the ALC boundary. <see cref="ReturnValue"/> carries a
/// live default-ALC object reference (e.g. the target's real object), which is exactly what we want.</summary>
public sealed class EvalResult
{
    public bool IsError { get; init; }
    public string? ErrorMessage { get; init; }
    public bool HasReturnValue { get; init; }
    public object? ReturnValue { get; init; }
}
