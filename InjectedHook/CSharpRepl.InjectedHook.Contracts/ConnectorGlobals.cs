// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;

namespace CSharpRepl.InjectedHook.Contracts;

/// <summary>
/// The globals object handed to the Roslyn submission chain in the engine. Its members become in-scope
/// identifiers inside every submission (exactly like the local REPL's ScriptGlobals), so a user can write
/// services.GetRequiredService&lt;T&gt;() or Get&lt;T&gt;() directly.
/// </summary>
public sealed class ConnectorGlobals
{
    /// <summary>The target's captured root service provider (null when no provider was captured).</summary>
    public IServiceProvider? services => ConnectorRoots.Services;

    /// <summary>Convenience: resolve a required service from the captured provider.</summary>
    public T Get<T>() where T : notnull
    {
        var provider = ConnectorRoots.Services
            ?? throw new InvalidOperationException("No service provider was captured from the target process.");
        var service = provider.GetService(typeof(T))
            ?? throw new InvalidOperationException($"No service of type {typeof(T)} is registered in the target.");
        return (T)service;
    }
}

/// <summary>
/// Static holder for the captured "roots" of the target process.
///
/// - Lives in the default ALC (shared back into the EngineALC) so the bootstrap's capture code and the
///   engine's submissions see the same instance.
/// - The bootstrap's HostCapture populates Services from the "Microsoft.Extensions.Hosting"
///   DiagnosticListener's HostBuilt event (Generic Host + WebApplication.CreateBuilder ASP.NET Core);
///   otherwise it stays null and only statics/framework code are reachable.
/// </summary>
public static class ConnectorRoots
{
    /// <summary>The target's captured root service provider, or null if none was captured.</summary>
    public static IServiceProvider? Services;
}
