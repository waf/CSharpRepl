// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Spike5.Contracts;

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
