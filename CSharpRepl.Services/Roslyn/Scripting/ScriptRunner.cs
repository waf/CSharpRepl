// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CSharpRepl.Services.Dotnet;
using CSharpRepl.Services.Roslyn.MetadataResolvers;
using CSharpRepl.Services.Roslyn.References;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Scripting;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Scripting;
using Microsoft.CodeAnalysis.Scripting.Hosting;
using Microsoft.CodeAnalysis.Text;

namespace CSharpRepl.Services.Roslyn.Scripting;

/// <summary>
/// Uses the Roslyn Scripting APIs to execute C# code in a string.
/// </summary>
internal sealed class ScriptRunner
{
    private readonly IConsoleEx console;
    private readonly InteractiveAssemblyLoader assemblyLoader;
    private readonly CompositeAlternativeReferenceResolver alternativeReferenceResolver;
    private readonly MetadataReferenceResolver metadataResolver;
    private readonly WorkspaceManager workspaceManager;
    private readonly AssemblyReferenceService referenceAssemblyService;
    private ScriptOptions scriptOptions;
    private ScriptState<object>? state;

    public ScriptRunner(
        WorkspaceManager workspaceManager,
        CSharpCompilationOptions compilationOptions,
        AssemblyReferenceService referenceAssemblyService,
        IConsoleEx console,
        Configuration configuration)
    {
        this.console = console;
        this.workspaceManager = workspaceManager;
        this.referenceAssemblyService = referenceAssemblyService;
        this.assemblyLoader = new InteractiveAssemblyLoader(new MetadataShadowCopyProvider());

        var dotnetBuilder = new DotnetBuilder(console);
        var solutionFileMetadataResolver = new SolutionFileMetadataResolver(dotnetBuilder, console);
        var nugetResolver = new NugetPackageMetadataResolver(console, configuration);

        this.alternativeReferenceResolver = new CompositeAlternativeReferenceResolver(
            nugetResolver,
            solutionFileMetadataResolver
        );

        this.metadataResolver = new CompositeMetadataReferenceResolver(
            nugetResolver,
            solutionFileMetadataResolver,
            new AssemblyReferenceMetadataResolver(console, referenceAssemblyService)
        );
        this.scriptOptions = ScriptOptions.Default
            .WithMetadataResolver(metadataResolver)
            .WithReferences(referenceAssemblyService.LoadedImplementationAssemblies)
            .WithAllowUnsafe(compilationOptions.AllowUnsafe)
            .WithLanguageVersion(LanguageVersion.Preview)
            .AddImports(compilationOptions.Usings);
    }

    /// <summary>
    /// Accepts a string containing C# code and runs it. Subsequent invocations will use the state from earlier invocations.
    /// </summary>
    public async Task<EvaluationResult> RunCompilation(string text, string[]? args = null, CancellationToken cancellationToken = default)
    {
        try
        {
            var alternativeResolutions = await alternativeReferenceResolver.GetAllAlternativeReferences(text, cancellationToken);
            if (alternativeResolutions.Length > 0)
            {
                this.scriptOptions = this.scriptOptions.WithReferences(scriptOptions.MetadataReferences.Concat(alternativeResolutions).DistinctBy(r => r.Display));
            }

            var usings = referenceAssemblyService.GetUsings(text);
            referenceAssemblyService.TrackUsings(usings);

            state = await EvaluateStringWithStateAsync(text, state, assemblyLoader, scriptOptions, args, cancellationToken).ConfigureAwait(false);

            return state.Exception is null
                ? await CreateSuccessfulResult(text, state, cancellationToken).ConfigureAwait(false)
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

    /// <summary>
    /// Compiles the provided code, with references to all previous script evaluations.
    /// However, the provided code is not run or persisted; future evaluations will not
    /// know about the code provided to this method.
    /// </summary>
    public Compilation CompileTransient(string code, OptimizationLevel optimizationLevel)
    {
        return CSharpCompilation.CreateScriptCompilation(
            "CompilationTransient",
            CSharpSyntaxTree.ParseText(code, CSharpParseOptions.Default.WithKind(SourceCodeKind.Script).WithLanguageVersion(LanguageVersion.Latest)),
            scriptOptions.MetadataReferences,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, usings: scriptOptions.Imports, optimizationLevel: optimizationLevel, allowUnsafe: scriptOptions.AllowUnsafe, metadataReferenceResolver: metadataResolver),
            previousScriptCompilation: state?.Script.GetCompilation() is CSharpCompilation previous ? previous : null,
            globalsType: typeof(ScriptGlobals)
        );
    }

    private async Task<EvaluationResult.Success> CreateSuccessfulResult(string text, ScriptState<object> state, CancellationToken cancellationToken)
    {
        var hasValueReturningStatement = await HasValueReturningStatement(text, cancellationToken).ConfigureAwait(false);

        referenceAssemblyService.AddImplementationAssemblyReferences(state.Script.GetCompilation().References);
        var frameworkReferenceAssemblies = referenceAssemblyService.LoadedReferenceAssemblies;
        var frameworkImplementationAssemblies = referenceAssemblyService.LoadedImplementationAssemblies;
        this.scriptOptions = this.scriptOptions.WithReferences(frameworkImplementationAssemblies);
        var returnValue = hasValueReturningStatement ? new Optional<object?>(state.ReturnValue) : default;
        return new EvaluationResult.Success(text, returnValue, frameworkImplementationAssemblies.Concat(frameworkReferenceAssemblies).ToList());
    }

    private async Task<ScriptState<object>> EvaluateStringWithStateAsync(string text, ScriptState<object>? state, InteractiveAssemblyLoader assemblyLoader, ScriptOptions scriptOptions, string[]? args, CancellationToken cancellationToken)
    {
        var scriptTask = state is null
            ? CSharpScript
                .Create(text, scriptOptions, globalsType: typeof(ScriptGlobals), assemblyLoader: assemblyLoader)
                .RunAsync(globals: CreateGlobalsObject(args), cancellationToken)
            : state
                .ContinueWithAsync(text, scriptOptions, cancellationToken);

        return await scriptTask.ConfigureAwait(false);
    }

    private async Task<bool> HasValueReturningStatement(string text, CancellationToken cancellationToken)
    {
        var sourceText = SourceText.From(text);
        var document = workspaceManager.CurrentDocument.WithText(sourceText);
        var tree = await document.GetSyntaxTreeAsync(cancellationToken).ConfigureAwait(false);
        if (tree != null &&
            await tree.GetRootAsync(cancellationToken).ConfigureAwait(false) is CompilationUnitSyntax root &&
            root.Members.Count > 0 &&
            root.Members.Last() is GlobalStatementSyntax { Statement: ExpressionStatementSyntax { SemicolonToken.IsMissing: true } possiblyValueReturningStatement })
        {
            //now we know the text's last statement does not have semicolon so it can return value
            //but the statement's return type still can be void - we need to find out
            var semanticModel = await document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);
            if (semanticModel != null)
            {
                var typeInfo = semanticModel.GetTypeInfo(possiblyValueReturningStatement.Expression, cancellationToken);
                return typeInfo.ConvertedType?.SpecialType != SpecialType.System_Void;
            }
        }
        return false;
    }

    private ScriptGlobals CreateGlobalsObject(string[]? args)
    {
        return new ScriptGlobals(console, args ?? Array.Empty<string>());
    }
}