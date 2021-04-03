using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Scripting;
using Microsoft.CodeAnalysis.Scripting;
using System;
using System.Threading.Tasks;

namespace LangRepl
{
    class ScriptRunner
    {
        private readonly ScriptOptions scriptOptions;
        private ScriptState<object> state;

        public ScriptRunner(CSharpCompilationOptions compilationOptions, PortableExecutableReference[] defaultReferences)
        {
            this.scriptOptions = ScriptOptions
                .Default
                .AddReferences(defaultReferences)
                .AddImports(compilationOptions.Usings);
        }

        public async Task<EvaluationResult> RunCompilation(string text)
        {
            try
            {
                state = await EvaluateStringWithStateAsync(text, this.state, this.scriptOptions);
                return state.Exception is null
                    ? new EvaluationResult.Success(state.ReturnValue)
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
        public record Success(object ReturnValue) : EvaluationResult;
    }
}
