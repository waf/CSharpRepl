// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Runtime.Loader;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Scripting;
using Microsoft.CodeAnalysis.Scripting;
using Microsoft.CodeAnalysis.Scripting.Hosting;
using Spike5.Contracts;

namespace Spike5.Engine;

/// <summary>Engine instrumented to report the single-file degradation: how many loaded assemblies have
/// no on-disk <c>Assembly.Location</c> (bundled), and therefore can't become a <c>MetadataReference</c>.</summary>
public sealed class InspectorEngine : IInspectorEngine
{
    private InteractiveAssemblyLoader assemblyLoader = null!;
    private ScriptOptions scriptOptions = null!;
    private ScriptState<object>? state;

    public void Initialize(bool registerLiveDependencies)
    {
        Console.WriteLine($"   [engine] corlib Location = '{typeof(object).Assembly.Location}'  (empty => bundled/self-contained single-file)");

        assemblyLoader = new InteractiveAssemblyLoader();
        var references = new List<MetadataReference>();
        var skipped = new List<string>();
        int total = 0, registered = 0;

        foreach (var asm in AssemblyLoadContext.Default.Assemblies)
        {
            total++;
            if (asm.IsDynamic) { skipped.Add(asm.GetName().Name + " (dynamic)"); continue; }
            if (string.IsNullOrEmpty(asm.Location)) { skipped.Add(asm.GetName().Name + " (no Location — bundled)"); continue; }
            try { references.Add(MetadataReference.CreateFromFile(asm.Location)); }
            catch (Exception e) { skipped.Add($"{asm.GetName().Name} ({e.GetType().Name})"); continue; }
            if (registerLiveDependencies) { try { assemblyLoader.RegisterDependency(asm); registered++; } catch { } }
        }

        Console.WriteLine($"   [engine] loaded assemblies: {total}, references built: {references.Count}, " +
                          $"registered: {registered}, skipped (no usable Location): {skipped.Count}");
        foreach (var s in skipped) Console.WriteLine($"        skipped: {s}");

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
            return new EvalResult { IsError = true, ErrorMessage = e.Message };
        }
    }
}
