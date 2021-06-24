// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Scripting;
using Microsoft.CodeAnalysis.Scripting;
using Microsoft.CodeAnalysis.Scripting.Hosting;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Threading;
using PrettyPrompt.Consoles;
using CSharpRepl.Services.Roslyn.MetadataResolvers;
using CSharpRepl.Services.Roslyn.References;

namespace CSharpRepl.Services.Roslyn
{
    /// <summary>
    /// Uses the Roslyn Scripting APIs to execute C# code in a string.
    /// </summary>
    internal sealed class ScriptRunner
    {
        private readonly InteractiveAssemblyLoader assemblyLoader;
        private readonly NugetPackageMetadataResolver nugetResolver;
        private readonly AssemblyReferenceService referenceAssemblyService;
        private ScriptOptions scriptOptions;
        private ScriptState<object>? state;

        public ScriptRunner(IConsole console, CSharpCompilationOptions compilationOptions, AssemblyReferenceService referenceAssemblyService)
        {
            this.referenceAssemblyService = referenceAssemblyService;
            this.assemblyLoader = new InteractiveAssemblyLoader(new MetadataShadowCopyProvider());
            this.nugetResolver = new NugetPackageMetadataResolver(console);

            this.scriptOptions = ScriptOptions.Default
                .WithMetadataResolver(new CompositeMetadataReferenceResolver(
                    nugetResolver,
                    new ProjectFileMetadataResolver(console),
                    new AssemblyReferenceMetadataResolver(console, referenceAssemblyService)
                ))
                .WithReferences(referenceAssemblyService.LoadedImplementationAssemblies)
                .WithAllowUnsafe(compilationOptions.AllowUnsafe)
                .AddImports(compilationOptions.Usings);
        }

        /// <summary>
        /// Accepts a string containing C# code and runs it. Subsequent invocations will use the state from earlier invocations.
        /// </summary>
        public async Task<EvaluationResult> RunCompilation(string text, string[]? args = null, CancellationToken cancellationToken = default)
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

                state = await EvaluateStringWithStateAsync(text, state, assemblyLoader, this.scriptOptions, args, cancellationToken).ConfigureAwait(false);

                return state.Exception is null
                    ? CreateSuccessfulResult(text, state)
                    : new EvaluationResult.Error(this.state.Exception);
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

        private EvaluationResult.Success CreateSuccessfulResult(string text, ScriptState<object> state)
        {
            referenceAssemblyService.AddImplementationAssemblyReferences(state.Script.GetCompilation().References);
            var frameworkReferenceAssemblies = referenceAssemblyService.LoadedReferenceAssemblies;
            var frameworkImplementationAssemblies = referenceAssemblyService.LoadedImplementationAssemblies;
            this.scriptOptions = this.scriptOptions.WithReferences(frameworkImplementationAssemblies);
            return new EvaluationResult.Success(text, state.ReturnValue, frameworkImplementationAssemblies.Concat(frameworkReferenceAssemblies).ToList());
        }

        private static Task<ScriptState<object>> EvaluateStringWithStateAsync(string text, ScriptState<object>? state, InteractiveAssemblyLoader assemblyLoader, ScriptOptions scriptOptions, string[]? args = null, CancellationToken cancellationToken = default)
        {
            return state is null
                ? CSharpScript
                    .Create(text, scriptOptions, globalsType: typeof(ScriptGlobals), assemblyLoader: assemblyLoader)
                    .RunAsync(globals: new ScriptGlobals { args = args ?? Array.Empty<string>() }, cancellationToken: cancellationToken)
                : state
                    .ContinueWithAsync(text, scriptOptions, cancellationToken: cancellationToken);
        }
    }

    public abstract record EvaluationResult
    {
        public sealed record Success(string Input, object ReturnValue, IReadOnlyCollection<MetadataReference> References) : EvaluationResult;
        public sealed record Error(Exception Exception) : EvaluationResult;
        public sealed record Cancelled() : EvaluationResult;
    }

    #pragma warning disable IDE1006 // Naming Styles, the properties in this class will be available as local variable in the script.
    /// <summary>
    /// Defines variables that are available in the C# Script environment.
    /// </summary>
    /// <remarks>Must be public so it can be referenced by the script</remarks>
    public sealed class ScriptGlobals
    {
        /// <summary>
        /// Arguments provided at the command line after a double dash.
        /// e.g. csharprepl -- argA argB argC
        /// </summary>
        public string[] args { get; set; } = Array.Empty<string>();
    }
    #pragma warning restore IDE1006 // Naming Styles
}
