// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Spike2.Contracts;

/// <summary>Where the bootstrap publishes the engine once it's up, so other code (a real pipe server,
/// here just the demo thread) can reach it. Lives in the default ALC.</summary>
public static class InspectorHost
{
    public static IInspectorEngine? Engine;
}

/// <summary>The cross-ALC engine contract (same shape as Spike #1, minus the bits not needed here).</summary>
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
