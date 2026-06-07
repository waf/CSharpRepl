// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Spike4.Contracts;

/// <summary>Captured roots. For a non-web Generic Host there is no IStartupFilter, so <see cref="Services"/>
/// is captured by snooping the "Microsoft.Extensions.Hosting" DiagnosticListener's HostBuilt event.</summary>
public static class InspectorRoots
{
    public static IServiceProvider? Services;
}

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
