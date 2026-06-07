// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Diagnostics;
using System.Reflection;
using System.Runtime.Loader;
using Spike4.Contracts;

namespace Spike4.Bootstrap;

internal static class EngineHost
{
    public static IInspectorEngine Load(string dir)
    {
        string engineDll = System.IO.Path.Combine(dir, "Spike4.Engine.dll");
        var alc = new EngineLoadContext(engineDll);
        var asm = alc.LoadFromAssemblyPath(engineDll);
        var type = asm.GetType("Spike4.Engine.InspectorEngine")
            ?? throw new InvalidOperationException("Spike4.Engine.InspectorEngine not found.");
        return (IInspectorEngine)Activator.CreateInstance(type)!;
    }

    public static void RunInspectorDemo(IInspectorEngine engine)
    {
        try
        {
            Console.WriteLine("   [inspector] waiting for the DiagnosticListener to capture the IServiceProvider...");
            var sw = Stopwatch.StartNew();
            while (InspectorRoots.Services is null && sw.Elapsed < TimeSpan.FromSeconds(10))
                Thread.Sleep(100);

            if (InspectorRoots.Services is null)
            {
                Console.WriteLine("   [inspector] FAIL: IServiceProvider was never captured (no HostBuilt event?)");
                return;
            }
            Console.WriteLine("   [inspector] root IServiceProvider captured; initializing engine");
            engine.Initialize(registerLiveDependencies: true);

            // Read the worker-driven singleton repeatedly: increasing values prove it's the SAME live
            // instance the in-process BackgroundService is mutating.
            var readings = new List<int>();
            for (int i = 0; i < 4; i++)
            {
                var v = Eval(engine, "Services.GetRequiredService<WorkerApp.ICounter>().Count");
                Console.WriteLine($"   [inspector] live WorkerApp.ICounter.Count = {v}");
                if (v is int n) readings.Add(n);
                Thread.Sleep(400);
            }
            bool live = readings.Count >= 2 && readings[^1] > readings[0];
            Console.WriteLine(live
                ? "   [inspector] PASS: engine reads the SAME live singleton the worker is mutating (count increased)"
                : "   [inspector] FAIL: count did not increase across reads");

            // Write back into the shared singleton; the worker prints when it observes Mark == 9999.
            Eval(engine, "Services.GetRequiredService<WorkerApp.ICounter>().Mark = 9999;");
            Console.WriteLine("   [inspector] wrote WorkerApp.ICounter.Mark = 9999 (watch for the worker's ack)");
            Thread.Sleep(500);

            Eval(engine, "Services.GetRequiredService<Microsoft.Extensions.Hosting.IHostApplicationLifetime>().StopApplication();");
            Console.WriteLine("   [inspector] requested graceful shutdown via captured provider");
        }
        catch (Exception e)
        {
            Console.WriteLine("   [inspector] demo failed: " + e);
        }
    }

    private static object? Eval(IInspectorEngine engine, string code)
    {
        var r = engine.EvalAsync(code).GetAwaiter().GetResult();
        return r.IsError ? "ERROR: " + r.ErrorMessage : r.ReturnValue;
    }
}

/// <summary>Isolated ALC for engine + Roslyn; shares Spike4.Contracts back to the default ALC.</summary>
internal sealed class EngineLoadContext : AssemblyLoadContext
{
    private readonly AssemblyDependencyResolver resolver;

    public EngineLoadContext(string engineMainDll) : base(name: "EngineALC", isCollectible: false)
        => resolver = new AssemblyDependencyResolver(engineMainDll);

    protected override Assembly? Load(AssemblyName assemblyName)
    {
        if (assemblyName.Name == "Spike4.Contracts")
            return null; // share with default ALC

        var path = resolver.ResolveAssemblyToPath(assemblyName);
        return path is not null ? LoadFromAssemblyPath(path) : null;
    }
}
