#region License Header
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.
#endregion
using Sharply.Services.Nuget;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Scripting;
using Microsoft.CodeAnalysis.Scripting;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Threading;
using Microsoft.CodeAnalysis.Scripting.Hosting;
using PrettyPrompt.Consoles;

namespace Sharply.Services.Roslyn
{
    class ScriptRunner
    {
        private readonly InteractiveAssemblyLoader assemblyLoader;
        private readonly NugetMetadataResolver nugetResolver;
        private ScriptOptions scriptOptions;
        private ScriptState<object> state;

        public ScriptRunner(IConsole console, CSharpCompilationOptions compilationOptions, ReferenceAssemblyService referenceAssemblyService)
        {
            this.assemblyLoader = new InteractiveAssemblyLoader(new MetadataShadowCopyProvider());
            this.nugetResolver = new NugetMetadataResolver(console, referenceAssemblyService.ImplementationAssemblyPaths);
            this.scriptOptions = ScriptOptions.Default
                .WithMetadataResolver(nugetResolver)
                .WithReferences(referenceAssemblyService.DefaultImplementationAssemblies)
                .WithAllowUnsafe(compilationOptions.AllowUnsafe)
                .AddImports(compilationOptions.Usings);
        }

        public async Task<EvaluationResult> RunCompilation(string text, CancellationToken cancellationToken)
        {
            try
            {
                var nugetCommands = text
                    .Split(new[] { '\r', '\n' }, StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                    .Where(nugetResolver.IsNugetReference);
                foreach (var nugetCommand in nugetCommands)
                {
                    var assemblyReferences = await nugetResolver.InstallNugetPackageAsync(nugetCommand, cancellationToken).ConfigureAwait(false);
                    this.scriptOptions = this.scriptOptions.AddReferences(assemblyReferences);
                }

                state = await EvaluateStringWithStateAsync(text, state, assemblyLoader, this.scriptOptions, cancellationToken).ConfigureAwait(false);
                var evaluatedReferences = state.Script.GetCompilation().References.ToList();

                return state.Exception is null
                    ? new EvaluationResult.Success(text, state.ReturnValue, evaluatedReferences)
                    : new EvaluationResult.Error(state.Exception);
            }
            catch (Exception oce) when (oce is OperationCanceledException || oce.InnerException is OperationCanceledException)
            {
                // user can cancel by pressing ctrl+c, which triggers the CancellationToken
                return new EvaluationResult.Cancelled();
            }
            catch (Exception exception)
            {
                return new EvaluationResult.Error(exception);
            }
        }

        private static Task<ScriptState<object>> EvaluateStringWithStateAsync(string text, ScriptState<object> state, InteractiveAssemblyLoader assemblyLoader, ScriptOptions scriptOptions, CancellationToken cancellationToken)
        {
            return state == null
                ? CSharpScript.Create(text, scriptOptions, assemblyLoader: assemblyLoader).RunAsync(cancellationToken: cancellationToken)
                : state.ContinueWithAsync(text, scriptOptions, cancellationToken: cancellationToken);
        }
    }

    public abstract record EvaluationResult
    {
        public record Success(string Input, object ReturnValue, IReadOnlyCollection<MetadataReference> References) : EvaluationResult;
        public record Error(Exception Exception) : EvaluationResult;
        public record Cancelled() : EvaluationResult;
    }
}
