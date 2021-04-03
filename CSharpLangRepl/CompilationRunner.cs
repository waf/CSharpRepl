using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Scripting;
using Microsoft.CodeAnalysis.Scripting;
using Microsoft.CodeAnalysis.Scripting.Hosting;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace CSharpLangRepl
{
    class CompilationRunner
    {
        private ScriptOptions scriptOptions;
        private ScriptState<object> state;


        public CompilationRunner(ScriptOptions scriptOptions)
        {
            this.scriptOptions = scriptOptions;
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
