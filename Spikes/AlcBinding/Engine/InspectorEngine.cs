// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Runtime.Loader;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Scripting;
using Microsoft.CodeAnalysis.Scripting;
using Microsoft.CodeAnalysis.Scripting.Hosting;
using Spike.Contracts;

namespace Spike.Engine;

/// <summary>
/// Trimmed relocation of the local REPL's ScriptRunner core (see CSharpRepl.Services ScriptRunner.cs).
/// Holds the ScriptState chain; builds references from the TARGET's loaded assemblies; optionally
/// registers them as live dependencies so submissions bind to the already-loaded instances.
/// </summary>
public sealed class InspectorEngine : IInspectorEngine
{
    private InteractiveAssemblyLoader assemblyLoader = null!;
    private ScriptOptions scriptOptions = null!;
    private ScriptState<object>? state;

    // Proof point: this typeof resolves in the EngineALC. The Host ReferenceEquals-compares it
    // against the default-ALC typeof(InspectorGlobals). Equal => contracts loaded exactly once.
    public object GlobalsType => typeof(InspectorGlobals);

    // The engine's Roslyn — used by the clash scenario to prove it differs from, and is isolated
    // from, any Roslyn the target itself loaded into the default ALC.
    public string RoslynVersion => typeof(CSharpSyntaxTree).Assembly.GetName().Version?.ToString() ?? "?";
    public string RoslynAlc => AssemblyLoadContext.GetLoadContext(typeof(CSharpSyntaxTree).Assembly)?.Name ?? "(Default)";

    public void Initialize(bool registerLiveDependencies)
    {
        Log($"--- engine Initialize(registerLiveDependencies: {registerLiveDependencies}) ---");
        Log($"engine running in ALC: {AssemblyLoadContext.GetLoadContext(typeof(InspectorEngine).Assembly)?.Name ?? "(Default)"}");

        this.assemblyLoader = new InteractiveAssemblyLoader();

        var references = new List<MetadataReference>();
        var skipped = new List<string>();
        int registered = 0;

        // Build references from the TARGET's live, loaded assemblies (the default ALC).
        // This is the realistic case: we don't know the target's types at compile time.
        foreach (var asm in AssemblyLoadContext.Default.Assemblies)
        {
            if (asm.IsDynamic) { skipped.Add($"{asm.GetName().Name} (dynamic)"); continue; }

            var location = asm.Location;
            if (string.IsNullOrEmpty(location))
            {
                // The documented single-file / in-memory limitation: no on-disk image to build a
                // MetadataReference from. Detect it cleanly rather than crashing.
                skipped.Add($"{asm.GetName().Name} (no on-disk location)");
                continue;
            }

            try
            {
                references.Add(MetadataReference.CreateFromFile(location));
            }
            catch (Exception e)
            {
                skipped.Add($"{asm.GetName().Name} ({e.GetType().Name})");
                continue;
            }

            if (registerLiveDependencies)
            {
                // The crux: tell Roslyn's loader this live Assembly instance satisfies the reference,
                // so the compiled submission binds to the already-loaded default-ALC object graph.
                try { assemblyLoader.RegisterDependency(asm); registered++; }
                catch (Exception e) { Log($"RegisterDependency failed for {asm.GetName().Name}: {e.GetType().Name}"); }
            }
        }

        Log($"references built: {references.Count}; registered as live deps: {registered}; skipped: {skipped.Count}");
        foreach (var s in skipped) Log($"   skipped: {s}");

        this.scriptOptions = ScriptOptions.Default
            .WithReferences(references)
            .WithImports("System")
            .WithLanguageVersion(LanguageVersion.Preview)
            .WithAllowUnsafe(true);
    }

    public async Task<EvalResult> EvalAsync(string code)
    {
        try
        {
            state = state is null
                ? await CSharpScript
                    .Create(code, scriptOptions, globalsType: typeof(InspectorGlobals), assemblyLoader: assemblyLoader)
                    .RunAsync(globals: new InspectorGlobals())
                : await state.ContinueWithAsync(code, scriptOptions);

            if (state.Exception is not null)
                return new EvalResult { IsError = true, ErrorMessage = state.Exception.ToString() };

            return new EvalResult { HasReturnValue = true, ReturnValue = state.ReturnValue };
        }
        catch (Exception e)
        {
            return new EvalResult { IsError = true, ErrorMessage = e.ToString() };
        }
    }

    private static void Log(string message) => Console.WriteLine($"      [engine] {message}");
}
