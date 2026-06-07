// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Reflection;
using System.Runtime.Loader;
using Spike5.Contracts;

namespace Spike5.Bootstrap;

internal static class EngineHost
{
    // The target's entry assembly simple name (used in the reflection-based access below).
    private const string TargetAssembly = "Target.Single";
    private const string ProgramType = "Target.Single.Program";

    public static IInspectorEngine Load(string dir)
    {
        string engineDll = System.IO.Path.Combine(dir, "Spike5.Engine.dll");
        var alc = new EngineLoadContext(engineDll);
        var asm = alc.LoadFromAssemblyPath(engineDll);
        var type = asm.GetType("Spike5.Engine.InspectorEngine")
            ?? throw new InvalidOperationException("Spike5.Engine.InspectorEngine not found.");
        return (IInspectorEngine)Activator.CreateInstance(type)!;
    }

    public static void RunInspectorDemo(IInspectorEngine engine)
    {
        try
        {
            Thread.Sleep(300); // let the target's Main set Counter = 1
            engine.Initialize(registerLiveDependencies: true);

            Console.WriteLine();
            Console.WriteLine("   [inspector] ===== single-file degradation report =====");

            Report(engine, "1 + 1",
                "framework-only eval (needs a corlib metadata reference)");

            Report(engine, $"{ProgramType}.Counter",
                "TYPED access to the target's static (needs a metadata ref to the BUNDLED app assembly)");

            string read = $"System.Type.GetType(\"{ProgramType}, {TargetAssembly}\")?.GetField(\"Counter\")?.GetValue(null)";
            Report(engine, read,
                "REFLECTION read of the same static (no metadata ref needed — binds to the live loaded type)");

            Thread.Sleep(700);
            Report(engine, read,
                "REFLECTION read again (should have increased => reading live state)");

            Report(engine, $"System.Type.GetType(\"{ProgramType}, {TargetAssembly}\")?.GetField(\"WriteProbe\")?.SetValue(null, 9999)",
                "REFLECTION write of WriteProbe = 9999 (the target's Main verifies it)");

            Console.WriteLine("   [inspector] ===== end report =====");
        }
        catch (Exception e)
        {
            Console.WriteLine("   [inspector] demo failed: " + e);
        }
    }

    private static void Report(IInspectorEngine engine, string code, string label)
    {
        var r = engine.EvalAsync(code).GetAwaiter().GetResult();
        Console.WriteLine($"   [inspector] {label}");
        Console.WriteLine($"        code:  {code}");
        if (r.IsError)
            Console.WriteLine($"        => COMPILE/RUNTIME ERROR: {FirstLine(r.ErrorMessage)}");
        else
            Console.WriteLine($"        => {r.ReturnValue ?? "(null/void)"}");
    }

    private static string FirstLine(string? s) => (s ?? "").Split('\n')[0].Trim();
}

internal sealed class EngineLoadContext : AssemblyLoadContext
{
    private readonly AssemblyDependencyResolver resolver;

    public EngineLoadContext(string engineMainDll) : base(name: "EngineALC", isCollectible: false)
        => resolver = new AssemblyDependencyResolver(engineMainDll);

    protected override Assembly? Load(AssemblyName assemblyName)
    {
        if (assemblyName.Name == "Spike5.Contracts")
            return null;
        var path = resolver.ResolveAssemblyToPath(assemblyName);
        return path is not null ? LoadFromAssemblyPath(path) : null;
    }
}
