using ReplDotNet.Nuget;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Scripting;
using Microsoft.CodeAnalysis.Scripting;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace ReplDotNet.Roslyn
{
    class ScriptRunner
    {
        private readonly NugetMetadataResolver nugetResolver;
        private readonly ScriptOptions scriptOptions;
        private ScriptState<object> state;

        public ScriptRunner(CSharpCompilationOptions compilationOptions, IReadOnlyCollection<MetadataReference> defaultImplementationAssemblies)
        {
            this.nugetResolver = new NugetMetadataResolver();
            this.scriptOptions = ScriptOptions
                .Default
                .WithMetadataResolver(nugetResolver)
                .WithReferences(defaultImplementationAssemblies)
                .AddImports(compilationOptions.Usings);
        }

        public async Task<EvaluationResult> RunCompilation(string text)
        {
            try
            {
                if(nugetResolver.IsNugetReference(text))
                {
                    var result = await nugetResolver.InstallNugetPackage(text);
                    state = await EvaluateStringWithStateAsync("", state, scriptOptions.AddReferences(result));
                    return new EvaluationResult.Success(text, null, result);
                }
                state = await EvaluateStringWithStateAsync(text, this.state, this.scriptOptions);
                var compilation = state.Script.GetCompilation();
                return state.Exception is null
                    ? new EvaluationResult.Success(text, state.ReturnValue, compilation.References.ToList())
                    : new EvaluationResult.Error(state.Exception);
            }
            catch (Exception exception)
            {
                return new EvaluationResult.Error(exception);
            }
        }

        private static Task<ScriptState<object>> EvaluateStringWithStateAsync(string text, ScriptState<object> state, ScriptOptions scriptOptions)
        {
            return state == null
                ? CSharpScript.Create(text, scriptOptions).RunAsync()
                : state.ContinueWithAsync(text, scriptOptions);
        }
    }

    public abstract record EvaluationResult
    {
        public record Error(Exception Exception) : EvaluationResult;
        public record Success(string Input, object ReturnValue, IReadOnlyCollection<MetadataReference> References) : EvaluationResult;
    }
}
