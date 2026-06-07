// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Runtime.CompilerServices;
using System.Runtime.Loader;

// NOTE: NO namespace. The .NET host startup-hook contract requires a type named exactly
// "StartupHook" with a "public static void Initialize()" method, in the global namespace.

/// <summary>
/// Runs (via DOTNET_STARTUP_HOOKS) before the target's Main. The crux this spike de-risks:
/// the bootstrap's only non-framework dependency (Spike2.Contracts) lives in the bootstrap dir,
/// which is NOT on the target's probing path. We must install a Default.Resolving handler pointing
/// at the bootstrap dir BEFORE any non-framework type is touched — hence the deferred StartInspector.
/// </summary>
internal static class StartupHook
{
    public static void Initialize()
    {
        // === Framework-only zone: nothing here may force a non-framework assembly load. ===
        Console.WriteLine("[hook] Initialize() running (this prints BEFORE the target's Main)");

        string dir = System.IO.Path.GetDirectoryName(typeof(StartupHook).Assembly.Location)!;
        Console.WriteLine($"[hook] bootstrap dir : {dir}");

        AssemblyLoadContext.Default.Resolving += (ctx, name) =>
        {
            string candidate = System.IO.Path.Combine(dir, name.Name + ".dll");
            return System.IO.File.Exists(candidate) ? ctx.LoadFromAssemblyPath(candidate) : null;
        };
        Console.WriteLine("[hook] installed Default.Resolving probing handler for the bootstrap dir");

        // === Handler is live; from here it's safe to touch Contracts/Engine types. ===
        // NoInlining guarantees this body is JIT-compiled (and its Contracts references resolved)
        // only when CALLED — i.e. after the line above ran — not when Initialize() is JITted.
        StartInspector(dir);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void StartInspector(string dir)
    {
        try
        {
            Console.WriteLine($"[hook] bootstrap statically references Roslyn? {Spike2.Bootstrap.EngineHost.BootstrapReferencesRoslyn()}");

            var engine = Spike2.Bootstrap.EngineHost.Load(dir);   // creates EngineALC, loads engine + Roslyn
            Spike2.Contracts.InspectorHost.Engine = engine;        // publish for any consumer
            Console.WriteLine("[hook] engine loaded into isolated EngineALC and published to InspectorHost");

            var thread = new System.Threading.Thread(() => Spike2.Bootstrap.EngineHost.RunInspectorDemo(engine))
            {
                IsBackground = true,
                Name = "inspector"
            };
            thread.Start();
        }
        catch (Exception e)
        {
            // The host must keep running even if the inspector fails to start.
            Console.WriteLine("[hook] inspector setup failed (host continues): " + e);
        }
    }
}
