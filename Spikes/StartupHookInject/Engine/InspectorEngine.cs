// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Runtime.Loader;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Scripting;
using Microsoft.CodeAnalysis.Scripting;
using Microsoft.CodeAnalysis.Scripting.Hosting;
using Spike2.Contracts;

namespace Spike2.Engine;

/// <summary>Same engine as Spike #1 (references from the target's loaded assemblies + RegisterDependency),
/// here driven from an injected startup hook in a real separate process. Scripts reference the target's
/// statics by full name (e.g. Target.Program.Counter), so no globals object is needed.</summary>
public sealed class InspectorEngine : IInspectorEngine
{
    private InteractiveAssemblyLoader assemblyLoader = null!;
    private ScriptOptions scriptOptions = null!;
    private ScriptState<object>? state;

    public void Initialize(bool registerLiveDependencies)
    {
        Console.WriteLine($"   [engine] running in ALC: {AssemblyLoadContext.GetLoadContext(typeof(InspectorEngine).Assembly)?.Name ?? "(Default)"}");
        Console.WriteLine($"   [engine] Roslyn version: {typeof(CSharpSyntaxTree).Assembly.GetName().Version} " +
                          $"in ALC {AssemblyLoadContext.GetLoadContext(typeof(CSharpSyntaxTree).Assembly)?.Name ?? "(Default)"}");

        assemblyLoader = new InteractiveAssemblyLoader();
        var references = new List<MetadataReference>();
        int registered = 0, skipped = 0;

        foreach (var asm in AssemblyLoadContext.Default.Assemblies)
        {
            if (asm.IsDynamic || string.IsNullOrEmpty(asm.Location)) { skipped++; continue; }
            try { references.Add(MetadataReference.CreateFromFile(asm.Location)); }
            catch { skipped++; continue; }
            if (registerLiveDependencies)
            {
                try { assemblyLoader.RegisterDependency(asm); registered++; } catch { }
            }
        }

        Console.WriteLine($"   [engine] references: {references.Count}, registered live deps: {registered}, skipped: {skipped}");

        scriptOptions = ScriptOptions.Default
            .WithReferences(references)
            .WithImports("System")
            .WithLanguageVersion(LanguageVersion.Preview);
    }

    public async Task<EvalResult> EvalAsync(string code)
    {
        try
        {
            state = state is null
                ? await CSharpScript.Create(code, scriptOptions, assemblyLoader: assemblyLoader).RunAsync()
                : await state.ContinueWithAsync(code, scriptOptions);

            return state.Exception is not null
                ? new EvalResult { IsError = true, ErrorMessage = state.Exception.ToString() }
                : new EvalResult { HasReturnValue = true, ReturnValue = state.ReturnValue };
        }
        catch (Exception e)
        {
            return new EvalResult { IsError = true, ErrorMessage = e.ToString() };
        }
    }
}
