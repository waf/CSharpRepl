// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Diagnostics;
using System.Net.Http;
using System.Reflection;
using System.Runtime.Loader;
using Spike3.Contracts;

namespace Spike3.Bootstrap;

internal static class EngineHost
{
    // The target's fixed test URL (the target binds this; the inspector posts to it). Spike-only.
    private const string TargetUrl = "http://127.0.0.1:52199";

    public static IInspectorEngine Load(string dir)
    {
        string engineDll = System.IO.Path.Combine(dir, "Spike3.Engine.dll");
        var alc = new EngineLoadContext(engineDll);
        var asm = alc.LoadFromAssemblyPath(engineDll);
        var type = asm.GetType("Spike3.Engine.InspectorEngine")
            ?? throw new InvalidOperationException("Spike3.Engine.InspectorEngine not found.");
        return (IInspectorEngine)Activator.CreateInstance(type)!;
    }

    public static void RunInspectorDemo(IInspectorEngine engine)
    {
        try
        {
            Console.WriteLine("   [inspector] waiting for the hosting-startup to capture the IServiceProvider...");
            var sw = Stopwatch.StartNew();
            while (InspectorRoots.Services is null && sw.Elapsed < TimeSpan.FromSeconds(20))
                Thread.Sleep(100);

            if (InspectorRoots.Services is null)
            {
                Console.WriteLine("   [inspector] FAIL: IServiceProvider was never captured");
                return;
            }
            Console.WriteLine("   [inspector] root IServiceProvider captured; initializing engine");
            engine.Initialize(registerLiveDependencies: true);

            var before = Eval(engine, "Services.GetRequiredService<Target.Web.IOrderService>().PendingCount");
            Console.WriteLine($"   [inspector] PendingCount before HTTP = {before}");

            int posts = 3;
            int ok = PostOrders(posts);
            Console.WriteLine($"   [inspector] issued {ok}/{posts} real HTTP POSTs to {TargetUrl}/order");

            var after = Eval(engine, "Services.GetRequiredService<Target.Web.IOrderService>().PendingCount");
            Console.WriteLine($"   [inspector] PendingCount after HTTP  = {after}");

            bool killer = before is 0 && after is int a && a == posts;
            Console.WriteLine(killer
                ? $"   [inspector] PASS: engine reads the SAME live singleton the request pipeline mutated (== {posts})"
                : $"   [inspector] FAIL: counts did not match (before={before}, after={after})");

            // Bonus: resolve a framework service through the captured provider and stop the app cleanly.
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

    private static int PostOrders(int n)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        int ok = 0;
        for (int i = 0; i < n; i++)
        {
            for (int attempt = 0; attempt < 50; attempt++)
            {
                try
                {
                    var resp = http.PostAsync(TargetUrl + "/order", null).GetAwaiter().GetResult();
                    if (resp.IsSuccessStatusCode) { ok++; break; }
                }
                catch { Thread.Sleep(100); } // server not listening yet — retry
            }
        }
        return ok;
    }
}

/// <summary>Isolated ALC for engine + Roslyn; shares Spike3.Contracts back to the default ALC.</summary>
internal sealed class EngineLoadContext : AssemblyLoadContext
{
    private readonly AssemblyDependencyResolver resolver;

    public EngineLoadContext(string engineMainDll) : base(name: "EngineALC", isCollectible: false)
        => resolver = new AssemblyDependencyResolver(engineMainDll);

    protected override Assembly? Load(AssemblyName assemblyName)
    {
        if (assemblyName.Name == "Spike3.Contracts")
            return null; // share with default ALC

        var path = resolver.ResolveAssemblyToPath(assemblyName);
        return path is not null ? LoadFromAssemblyPath(path) : null;
    }
}
