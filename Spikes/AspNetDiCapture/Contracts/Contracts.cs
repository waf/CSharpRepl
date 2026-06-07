// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Spike3.Contracts;

/// <summary>Captured roots of the target. <see cref="Services"/> is the app's root IServiceProvider,
/// captured cleanly by the hosting-startup IStartupFilter (no reflection).</summary>
public static class InspectorRoots
{
    public static IServiceProvider? Services;
}

/// <summary>Globals handed to the submission chain. Scripts use `Services.GetRequiredService&lt;T&gt;()`
/// exactly like the verification plan describes.</summary>
public sealed class InspectorGlobals
{
    public IServiceProvider? Services => InspectorRoots.Services;
    public T Get<T>() => (T)InspectorRoots.Services!.GetService(typeof(T))!;
}

public static class InspectorHost
{
    public static IInspectorEngine? Engine;
}

public interface IInspectorEngine
{
    void Initialize(bool registerLiveDependencies);
    Task<EvalResult> EvalAsync(string code);
}

public sealed class EvalResult
{
    public bool IsError { get; init; }
    public string? ErrorMessage { get; init; }
    public bool HasReturnValue { get; init; }
    public object? ReturnValue { get; init; }
}
