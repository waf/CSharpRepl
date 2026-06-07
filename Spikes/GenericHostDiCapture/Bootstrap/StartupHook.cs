// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Runtime.CompilerServices;
using System.Runtime.Loader;

// NO namespace (host startup-hook contract).

/// <summary>For a non-web Generic Host: install the DiagnosticListener snoop BEFORE Main (so it's live
/// before the host is built), then load the engine.</summary>
internal static class StartupHook
{
    public static void Initialize()
    {
        Console.WriteLine("[hook] Initialize() running (before Main)");
        string dir = System.IO.Path.GetDirectoryName(typeof(StartupHook).Assembly.Location)!;

        AssemblyLoadContext.Default.Resolving += (ctx, name) =>
        {
            string candidate = System.IO.Path.Combine(dir, name.Name + ".dll");
            return System.IO.File.Exists(candidate) ? ctx.LoadFromAssemblyPath(candidate) : null;
        };

        // Subscribe to the hosting DiagnosticListener NOW, before the host is built in Main.
        Spike4.Bootstrap.HostCapture.Install();

        StartInspector(dir);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void StartInspector(string dir)
    {
        try
        {
            var engine = Spike4.Bootstrap.EngineHost.Load(dir);
            Spike4.Contracts.InspectorHost.Engine = engine;
            new System.Threading.Thread(() => Spike4.Bootstrap.EngineHost.RunInspectorDemo(engine))
            {
                IsBackground = true,
                Name = "inspector"
            }.Start();
        }
        catch (Exception e)
        {
            Console.WriteLine("[hook] inspector setup failed (host continues): " + e);
        }
    }
}
