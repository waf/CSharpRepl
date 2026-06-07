// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Runtime.Loader;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Scripting;
using Microsoft.CodeAnalysis.Scripting;
using Microsoft.CodeAnalysis.Scripting.Hosting;
using Spike3.Contracts;

namespace Spike3.Engine;

/// <summary>Roslyn engine with globals wired to the captured IServiceProvider, so scripts can write
/// `Services.GetRequiredService&lt;T&gt;()`.</summary>
public sealed class InspectorEngine : IInspectorEngine
{
    private InteractiveAssemblyLoader assemblyLoader = null!;
    private ScriptOptions scriptOptions = null!;
    private ScriptState<object>? state;

    public void Initialize(bool registerLiveDependencies)
    {
        Console.WriteLine($"   [engine] ALC={AssemblyLoadContext.GetLoadContext(typeof(InspectorEngine).Assembly)?.Name}, " +
                          $"Roslyn v{typeof(CSharpSyntaxTree).Assembly.GetName().Version} in {AssemblyLoadContext.GetLoadContext(typeof(CSharpSyntaxTree).Assembly)?.Name}");

        assemblyLoader = new InteractiveAssemblyLoader();
        var references = new List<MetadataReference>();
        int registered = 0, skipped = 0;
        foreach (var asm in AssemblyLoadContext.Default.Assemblies)
        {
            if (asm.IsDynamic || string.IsNullOrEmpty(asm.Location)) { skipped++; continue; }
            try { references.Add(MetadataReference.CreateFromFile(asm.Location)); }
            catch { skipped++; continue; }
            if (registerLiveDependencies) { try { assemblyLoader.RegisterDependency(asm); registered++; } catch { } }
        }
        Console.WriteLine($"   [engine] references={references.Count}, registered live deps={registered}, skipped={skipped}");

        scriptOptions = ScriptOptions.Default
            .WithReferences(references)
            .WithImports("System", "Microsoft.Extensions.DependencyInjection")
            .WithLanguageVersion(LanguageVersion.Preview);
    }

    public async Task<EvalResult> EvalAsync(string code)
    {
        try
        {
            state = state is null
                ? await CSharpScript.Create(code, scriptOptions, globalsType: typeof(InspectorGlobals), assemblyLoader: assemblyLoader)
                                    .RunAsync(globals: new InspectorGlobals())
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
