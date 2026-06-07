// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;

namespace CSharpRepl.Inspector.Contracts;

/// <summary>
/// The globals object handed to the Roslyn submission chain in the engine. Its members become in-scope
/// identifiers inside every submission (exactly like the local REPL's ScriptGlobals), so a user can write
/// <c>services.GetRequiredService&lt;T&gt;()</c> or <c>Get&lt;T&gt;()</c> directly.
/// </summary>
public sealed class InspectorGlobals
{
    /// <summary>The target's captured root service provider (null until DI capture lands in M2).</summary>
    public IServiceProvider? services => InspectorRoots.Services;

    /// <summary>Convenience: resolve a required service from the captured provider.</summary>
    public T Get<T>() where T : notnull
    {
        var provider = InspectorRoots.Services
            ?? throw new InvalidOperationException("No service provider was captured from the target process.");
        var service = provider.GetService(typeof(T))
            ?? throw new InvalidOperationException($"No service of type {typeof(T)} is registered in the target.");
        return (T)service;
    }
}
