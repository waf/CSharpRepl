// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Reflection;
using System.Runtime.Loader;
using Spike2.Contracts;

namespace Spike2.Bootstrap;

/// <summary>Loads the engine into an isolated ALC (no Roslyn touched here) and runs the demo that
/// reads + writes the target's live statics from injected code.</summary>
internal static class EngineHost
{
    public static bool BootstrapReferencesRoslyn() =>
        typeof(EngineHost).Assembly
            .GetReferencedAssemblies()
            .Any(a => a.Name?.StartsWith("Microsoft.CodeAnalysis", StringComparison.Ordinal) == true);

    public static IInspectorEngine Load(string dir)
    {
        string engineDll = System.IO.Path.Combine(dir, "Spike2.Engine.dll");
        var alc = new EngineLoadContext(engineDll);
        var engineAsm = alc.LoadFromAssemblyPath(engineDll);
        var engineType = engineAsm.GetType("Spike2.Engine.InspectorEngine")
            ?? throw new InvalidOperationException("Spike2.Engine.InspectorEngine not found in " + engineDll);
        return (IInspectorEngine)Activator.CreateInstance(engineType)!;
    }

    public static void RunInspectorDemo(IInspectorEngine engine)
    {
        try
        {
            Console.WriteLine("   [inspector] background thread started; initializing engine...");
            engine.Initialize(registerLiveDependencies: true);

            // Read the target's live static repeatedly while the target's Main mutates it.
            for (int i = 0; i < 4; i++)
            {
                System.Threading.Thread.Sleep(400);
                var r = engine.EvalAsync("Target.Program.Counter").GetAwaiter().GetResult();
                Console.WriteLine($"   [inspector] read live Target.Program.Counter = {Show(r)}");
            }

            // Write back into the target's real static; the target's Main verifies it saw 9999.
            var w = engine.EvalAsync("Target.Program.WriteProbe = 9999; Target.Program.WriteProbe")
                          .GetAwaiter().GetResult();
            Console.WriteLine($"   [inspector] wrote Target.Program.WriteProbe (engine re-read = {Show(w)})");
        }
        catch (Exception e)
        {
            Console.WriteLine("   [inspector] demo failed: " + e);
        }
    }

    private static string Show(EvalResult r) =>
        r.IsError ? "ERROR: " + r.ErrorMessage
        : r.HasReturnValue ? (r.ReturnValue?.ToString() ?? "null")
        : "(void)";
}

/// <summary>Isolated ALC for engine + Roslyn; shares Spike2.Contracts back to the default ALC.</summary>
internal sealed class EngineLoadContext : AssemblyLoadContext
{
    private readonly AssemblyDependencyResolver resolver;

    public EngineLoadContext(string engineMainDll) : base(name: "EngineALC", isCollectible: false)
        => resolver = new AssemblyDependencyResolver(engineMainDll);

    protected override Assembly? Load(AssemblyName assemblyName)
    {
        if (assemblyName.Name == "Spike2.Contracts")
            return null; // share with default ALC (resolved there via the bootstrap's Resolving handler)

        var path = resolver.ResolveAssemblyToPath(assemblyName);
        return path is not null ? LoadFromAssemblyPath(path) : null;
    }
}
