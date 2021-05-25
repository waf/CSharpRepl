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

namespace Sharply.Services.Roslyn
{
    class ScriptRunner
    {
        private readonly NugetMetadataResolver nugetResolver;
        private ScriptOptions scriptOptions;
        private ScriptState<object> state;

        public ScriptRunner(CSharpCompilationOptions compilationOptions, ReferenceAssemblyService referenceAssemblyService)
        {
            this.nugetResolver = new NugetMetadataResolver(referenceAssemblyService.ImplementationAssemblyPaths);
            this.scriptOptions = ScriptOptions.Default
                .WithMetadataResolver(nugetResolver)
                .WithReferences(referenceAssemblyService.DefaultImplementationAssemblies)
                .WithEmitDebugInformation(true)
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
                    var nugetReferences = await nugetResolver.InstallNugetPackage(nugetCommand, cancellationToken).ConfigureAwait(false);
                    this.scriptOptions = this.scriptOptions.AddReferences(nugetReferences);
                }

                state = await EvaluateStringWithStateAsync(text, state, this.scriptOptions, cancellationToken).ConfigureAwait(false);
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

        public Script<object> CreateCompilation(string text) =>
             CSharpScript.Create(text, scriptOptions);

        private static Task<ScriptState<object>> EvaluateStringWithStateAsync(string text, ScriptState<object> state, ScriptOptions scriptOptions, CancellationToken cancellationToken)
        {
            return state == null
                ? CSharpScript.Create(text, scriptOptions).RunAsync(cancellationToken: cancellationToken)
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
