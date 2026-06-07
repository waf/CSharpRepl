// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Reflection;
using System.Runtime.Loader;
using Spike.Contracts;
using Spike.TargetLib;

namespace Spike.Host;

/// <summary>
/// Single-process, two-ALC harness that proves the crux of the inspector architecture:
/// a Roslyn engine running in an ISOLATED AssemblyLoadContext emits submission assemblies that
/// (a) bind to LIVE object instances living in the DEFAULT ALC, and (b) preserve type identity
/// for both the shared contracts/globals type AND the target's own types.
///
/// No pipes, no separate process, no startup hook — just the novel risk, in isolation.
/// </summary>
internal static class Program
{
    private static int failures;

    private static async Task<int> Main()
    {
        Console.WriteLine("=================================================================");
        Console.WriteLine(" ALC cross-context live-binding spike");
        Console.WriteLine("=================================================================");

        var engineDir = Path.Combine(AppContext.BaseDirectory, "engine");
        var engineDll = Path.Combine(engineDir, "Spike.Engine.dll");
        Console.WriteLine($"Host running in ALC : {AssemblyLoadContext.GetLoadContext(typeof(Program).Assembly)?.Name ?? "(Default)"}");
        Console.WriteLine($"Engine DLL          : {engineDll}");
        Console.WriteLine($"Engine DLL exists   : {File.Exists(engineDll)}");
        if (!File.Exists(engineDll))
        {
            Console.WriteLine("FATAL: engine not found. Build the Host project (the CopyEngine target stages it).");
            return 2;
        }

        // Scenario A is the architecture's actual bet: Roslyn isolated + RegisterDependency.
        await RunScenario("A: WITH RegisterDependency (the design's bet)", engineDll, registerLiveDependencies: true);

        // Scenario B probes whether RegisterDependency is load-bearing, or whether the
        // EngineALC -> Default fallback alone is enough for live binding.
        await RunScenario("B: WITHOUT RegisterDependency (is it load-bearing?)", engineDll, registerLiveDependencies: false);

        // Scenario C is the reason the isolated-ALC design exists: a target that itself has a
        // DIFFERENT Roslyn loaded in its default ALC must not clash with the engine's Roslyn.
        await RunClashScenario("C: Roslyn-version clash (the reason isolation exists)", engineDll);

        Console.WriteLine();
        Console.WriteLine("=================================================================");
        Console.WriteLine(failures == 0
            ? " RESULT: ALL CRUX ASSERTIONS PASSED — architecture de-risked."
            : $" RESULT: {failures} ASSERTION(S) FAILED — see above.");
        Console.WriteLine("=================================================================");
        return failures == 0 ? 0 : 1;
    }

    private static async Task RunScenario(string label, string engineDll, bool registerLiveDependencies)
    {
        Banner(label);

        var liveCounter = new Counter();
        InspectorRoots.Service = liveCounter;
        Console.WriteLine($"[host] created live Counter in default ALC: {liveCounter} (id {System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(liveCounter)})");

        var engine = SetupEngine(engineDll, registerLiveDependencies);
        if (engine is null) return;

        await RunChainAndAssert(engine, liveCounter);
    }

    private static async Task RunClashScenario(string label, string engineDll)
    {
        Banner(label);

        // 1. The TARGET uses its OWN (older) Roslyn — loading it into the default ALC.
        var (hostVersion, hostAlc) = UseHostRoslyn();
        Console.WriteLine($"[host] target's own Roslyn : v{hostVersion} in ALC {hostAlc}");
        Check("target's own Roslyn loaded into the DEFAULT ALC", hostAlc == "Default");

        var liveCounter = new Counter();
        InspectorRoots.Service = liveCounter;

        var engine = SetupEngine(engineDll, registerLiveDependencies: true);
        if (engine is null) return;

        // 2. The engine's Roslyn must be a DIFFERENT version, isolated in EngineALC.
        Console.WriteLine($"[host] engine's Roslyn     : v{engine.RoslynVersion} in ALC {engine.RoslynAlc}");
        Check("engine's Roslyn is isolated in EngineALC", engine.RoslynAlc == "EngineALC");
        Check($"engine Roslyn (v{engine.RoslynVersion}) differs from target Roslyn (v{hostVersion})",
            engine.RoslynVersion != hostVersion);

        // 3. THE POINT: live binding still works with a competing Roslyn sitting in the default ALC.
        await RunChainAndAssert(engine, liveCounter);

        // 4. Coexistence both ways: the target can STILL use its own Roslyn after the engine ran.
        var (hostVersionAfter, _) = UseHostRoslyn();
        Check("target's own Roslyn still usable after engine ran (no clash either direction)",
            hostVersionAfter == hostVersion);
    }

    /// <summary>Sets up an engine in a fresh isolated ALC and runs the cross-boundary identity checks.
    /// Returns null if the contracts-identity cast failed (a hard stop for that scenario).</summary>
    private static IInspectorEngine? SetupEngine(string engineDll, bool registerLiveDependencies)
    {
        var alc = new EngineLoadContext(engineDll);
        var engineAsm = alc.LoadFromAssemblyPath(engineDll);
        Console.WriteLine($"[host] engine assembly ALC : {AssemblyLoadContext.GetLoadContext(engineAsm)?.Name ?? "(Default)"}");

        var engineType = engineAsm.GetType("Spike.Engine.InspectorEngine")
            ?? throw new InvalidOperationException("Spike.Engine.InspectorEngine not found.");
        object engineInstance = Activator.CreateInstance(engineType)!;

        // CRUX #0: casting across the boundary. If Contracts loaded twice, this throws.
        IInspectorEngine engine;
        try
        {
            engine = (IInspectorEngine)engineInstance;
            Pass("contracts type identity: (IInspectorEngine) cast across ALC boundary succeeded");
        }
        catch (InvalidCastException)
        {
            Fail("contracts type identity: cast to IInspectorEngine FAILED — contracts assembly loaded twice");
            return null;
        }

        // CRUX #1: the globals type is the SAME Type object on both sides.
        Check("globals type identity (typeof(InspectorGlobals) ReferenceEquals across ALC)",
            ReferenceEquals(typeof(InspectorGlobals), engine.GlobalsType));

        engine.Initialize(registerLiveDependencies);
        return engine;
    }

    /// <summary>Runs the shared multi-submission chain and the killer live-binding assertions.</summary>
    private static async Task RunChainAndAssert(IInspectorEngine engine, Counter liveCounter)
    {
        // Submission 2 reuses the var from #1; submission 4 reuses a method declared in #3 —
        // proving full local-REPL parity via ScriptState.ContinueWithAsync.
        await Eval(engine, "var c = (Spike.TargetLib.Counter)Service; c.Inc(); c.Inc();", expectReturn: false);

        var count = await Eval(engine, "c.Count");
        Check("submission reads mutated state via reused var (c.Count == 2)", Equals(count, 2));

        await Eval(engine, "int Times10(int n) => n * 10;", expectReturn: false);
        var times10 = await Eval(engine, "Times10(c.Count)");
        Check("declared method + var both persist across submissions (Times10(c.Count) == 20)", Equals(times10, 20));

        var submissionAlc = await Eval(engine,
            "var __a = System.Reflection.Assembly.GetExecutingAssembly();" +
            "var __ctx = System.Runtime.Loader.AssemblyLoadContext.GetLoadContext(__a);" +
            "(__ctx is null ? \"NULL-CONTEXT\" : (\"ALC=\" + (__ctx.Name ?? \"<unnamed>\"))) + \" | asm=\" + __a.GetName().Name");
        Console.WriteLine($"[host] submission assembly load context: {submissionAlc}");

        var returned = await Eval(engine, "c");

        // ===== THE KILLER ASSERTIONS =====
        Check($"KILLER: default-ALC sees live mutation (InspectorRoots.Service.Count == 2, actual {liveCounter.Count})",
            liveCounter.Count == 2);
        Check("KILLER: ReferenceEquals(returned object, host's live Counter)",
            ReferenceEquals(returned, liveCounter));

        Console.WriteLine($"[host] final Counter (default ALC view): {liveCounter}");
    }

    // ---- helpers -------------------------------------------------------------------------------

    /// <summary>Forces the target's own (older) Roslyn to load + run in the default ALC, and reports it.</summary>
    private static (string version, string alc) UseHostRoslyn()
    {
        var tree = Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree.ParseText("class HostProbe { int X => 41 + 1; }");
        _ = tree.GetRoot().DescendantNodes().Count(); // force real parse work
        var asm = typeof(Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree).Assembly;
        return (asm.GetName().Version?.ToString() ?? "?",
                AssemblyLoadContext.GetLoadContext(asm)?.Name ?? "(Default)");
    }

    private static async Task<object?> Eval(IInspectorEngine engine, string code, bool expectReturn = true)
    {
        var result = await engine.EvalAsync(code);
        if (result.IsError)
        {
            Fail($"eval threw for `{code}`:\n{Indent(result.ErrorMessage)}");
            return null;
        }
        var shown = result.HasReturnValue ? Render(result.ReturnValue) : "(void)";
        Console.WriteLine($"[host] eval `{code}` => {shown}");
        return result.ReturnValue;
    }

    private static string Render(object? v) => v switch
    {
        null => "null",
        string s => $"\"{s}\"",
        _ => $"{v} : {v.GetType().FullName}"
    };

    private static void Banner(string label)
    {
        Console.WriteLine();
        Console.WriteLine("----------------------------------------------------------------");
        Console.WriteLine($" Scenario {label}");
        Console.WriteLine("----------------------------------------------------------------");
    }

    private static void Check(string what, bool ok)
    {
        if (ok) Pass(what); else Fail(what);
    }

    private static void Pass(string what) => Console.WriteLine($"   PASS  {what}");

    private static void Fail(string what)
    {
        failures++;
        Console.WriteLine($"   FAIL  {what}");
    }

    private static string Indent(string? s) =>
        string.Join('\n', (s ?? "").Split('\n').Select(l => "         " + l));
}

/// <summary>
/// Isolated load context for the engine + Roslyn. Resolves engine/Roslyn from its own directory
/// (via the engine's deps.json), but DELEGATES the shared contracts assembly (and, by null-fallback,
/// the framework) back to the default ALC so those types stay identical across the boundary.
/// </summary>
internal sealed class EngineLoadContext : AssemblyLoadContext
{
    private readonly AssemblyDependencyResolver resolver;

    public EngineLoadContext(string engineMainDll) : base(name: "EngineALC", isCollectible: false)
        => resolver = new AssemblyDependencyResolver(engineMainDll);

    protected override Assembly? Load(AssemblyName assemblyName)
    {
        // Force-share the contracts assembly. A copy of Spike.Contracts.dll physically exists in the
        // engine directory (it's an Engine dependency); without this guard the resolver would load a
        // SECOND copy here, breaking type identity. Returning null falls back to the default ALC.
        if (assemblyName.Name == "Spike.Contracts")
            return null;

        var path = resolver.ResolveAssemblyToPath(assemblyName);
        return path is not null ? LoadFromAssemblyPath(path) : null;
    }
}
