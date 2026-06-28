// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;

// NOTE: NO namespace. The .NET host startup-hook contract requires a type named exactly "StartupHook"
// with a "public static void Initialize()" method, in the global namespace.

/// <summary>
/// Runs (via DOTNET_STARTUP_HOOKS) before the target's Main, in the target's default ALC.
///
/// - Contracts (the only non-framework dependency) lives in the bootstrap directory, which is NOT on the
///   target's probing path — so a Default.Resolving handler is installed before touching any non-framework type.
/// - All real work is deferred to Start, marked NoInlining so its Contracts references are JIT-resolved
///   only after the handler is live.
/// </summary>
internal static class StartupHook
{
    public static void Initialize()
    {
        try
        {
            // === Framework-only zone: nothing here may force a non-framework assembly load. ===
            var dir = Path.GetDirectoryName(typeof(StartupHook).Assembly.Location);
            if (string.IsNullOrEmpty(dir))
            {
                return; // single-file bootstrap would have no location; we never bundle it
            }

            AssemblyLoadContext.Default.Resolving += (context, name) =>
            {
                // Scoped to connector assemblies so we can't accidentally shadow the target's own resolution.
                if (name.Name is null || !name.Name.StartsWith("CSharpRepl.InjectedHook", StringComparison.Ordinal))
                {
                    return null;
                }

                var candidate = Path.Combine(dir, name.Name + ".dll");
                return File.Exists(candidate) ? context.LoadFromAssemblyPath(candidate) : null;
            };

            // === Handler is live; safe to touch Contracts/engine types from here. ===
            Start(dir);
        }
        catch
        {
            // The host must keep running even if the connector fails to start. Swallow everything.
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void Start(string bootstrapDirectory)
    {
        // Subscribe to the hosting DiagnosticListener now, before the target's Main builds its host, so the
        // root IServiceProvider is captured for `services`/Get<T>() (see HostCapture).
        CSharpRepl.InjectedHook.HostCapture.Install();

        var engine = CSharpRepl.InjectedHook.EngineHost.Load(bootstrapDirectory);
        CSharpRepl.InjectedHook.ConnectorServer.StartInBackground(engine);
    }
}
