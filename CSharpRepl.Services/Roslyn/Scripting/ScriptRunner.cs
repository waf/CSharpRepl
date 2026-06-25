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
    private readonly IConsoleService console;
    private readonly InteractiveAssemblyLoader assemblyLoader;
    private readonly CompositeAlternativeReferenceResolver alternativeReferenceResolver;
    private readonly MetadataReferenceResolver metadataResolver;
    private readonly WorkspaceManager workspaceManager;
    private readonly CSharpParseOptions parseOptions;
    private readonly AssemblyReferenceService referenceAssemblyService;
    private readonly Type globalsType;
    private ScriptOptions scriptOptions;
    private ScriptState<object>? state;

    public ScriptRunner(
        WorkspaceManager workspaceManager,
        CSharpParseOptions parseOptions,
        CSharpCompilationOptions compilationOptions,
        AssemblyReferenceService referenceAssemblyService,
        IConsoleService console,
        Configuration configuration,
        Type? globalsType = null)
    {
        this.console = console;
        this.workspaceManager = workspaceManager;
        this.parseOptions = parseOptions;
        this.referenceAssemblyService = referenceAssemblyService;
        // The globals type backs CompileTransient's semantic model (used for symbol exploration). It defaults
        // to the local ScriptGlobals; the inspector's remote editor passes InspectorGlobals so symbol lookups
        // over `services` resolve. The live-evaluation path below always uses ScriptGlobals (it's local-only).
        this.globalsType = globalsType ?? typeof(ScriptGlobals);
        this.assemblyLoader = new InteractiveAssemblyLoader(new MetadataShadowCopyProvider());
        // Owns runtime loading of #r'd assemblies (one load context + the resolve fallback). Threaded into both the
        // nuget resolver (to pin each restored assembly to one instance, #355) and the assembly-reference resolver
        // (to record #r'd DLLs' deps.json for runtime resolution of their transitive deps).
        var replAssemblyLoader = new ReplAssemblyLoader(assemblyLoader, referenceAssemblyService, console);

        var dotnetBuilder = new DotnetBuilder(console);
        var solutionFileMetadataResolver = new SolutionFileMetadataResolver(dotnetBuilder, console);
        var nugetResolver = new NugetPackageMetadataResolver(console, configuration, referenceAssemblyService, replAssemblyLoader);

        this.alternativeReferenceResolver = new CompositeAlternativeReferenceResolver(
            nugetResolver,
            solutionFileMetadataResolver
        );

        this.metadataResolver = new CompositeMetadataReferenceResolver(
            nugetResolver,
            solutionFileMetadataResolver,
            new AssemblyReferenceMetadataResolver(referenceAssemblyService, replAssemblyLoader)
        );
        this.scriptOptions = ScriptOptions.Default
            .WithMetadataResolver(metadataResolver)
            .WithSourceResolver(compilationOptions.SourceReferenceResolver ?? SourceFileResolver.Default)
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
                // Run a empty base submission if we don't already have one, so the core library is resolved from the implementation assemblies.
                // This normally happens from the WarmUp routine, but if we evaluate a project/solution reference instead (-r Foo.csproj), it would
                // load the framework _reference_ assemblies (e.g. Microsoft.NETCore.App.Ref), causing the error from https://github.com/waf/CSharpRepl/issues/399
                state ??= await EvaluateStringWithStateAsync(string.Empty, state: null, assemblyLoader, scriptOptions, args, cancellationToken).ConfigureAwait(false);

                // Unify by assembly identity (not just by path), so conflicting versions pulled in by different
                // `#r` closures collapse to one - see RemoveDuplicateReferences. https://github.com/waf/CSharpRepl/issues/355
                this.scriptOptions = this.scriptOptions.WithReferences(referenceAssemblyService.RemoveDuplicateReferences(scriptOptions.MetadataReferences.Concat(alternativeResolutions)));
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
            CSharpSyntaxTree.ParseText(code, parseOptions),
            scriptOptions.MetadataReferences,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, usings: scriptOptions.Imports, optimizationLevel: optimizationLevel, allowUnsafe: scriptOptions.AllowUnsafe, metadataReferenceResolver: metadataResolver),
            previousScriptCompilation: state?.Script.GetCompilation() is CSharpCompilation previous ? previous : null,
            globalsType: globalsType
        );
    }

    private async Task<EvaluationResult.Success> CreateSuccessfulResult(string text, ScriptState<object> state, CancellationToken cancellationToken)
    {
        var hasValueReturningStatement = (await HasValueReturningStatement(text, cancellationToken).ConfigureAwait(false)).HasValue;

        referenceAssemblyService.AddImplementationAssemblyReferences(state.Script.GetCompilation().References);
        var frameworkReferenceAssemblies = referenceAssemblyService.LoadedReferenceAssemblies;
        var frameworkImplementationAssemblies = referenceAssemblyService.LoadedImplementationAssemblies;
        // The accumulated implementation assemblies are deduplicated only by path, so re-unify by identity before
        // they become the next submission's references. https://github.com/waf/CSharpRepl/issues/355
        this.scriptOptions = this.scriptOptions.WithReferences(referenceAssemblyService.RemoveDuplicateReferences(frameworkImplementationAssemblies));
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

    internal async Task<(ExpressionSyntax Expression, ITypeSymbol Type)?> HasValueReturningStatement(string text, CancellationToken cancellationToken)
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
                var returnType = semanticModel.GetTypeInfo(possiblyValueReturningStatement.Expression, cancellationToken).ConvertedType;
                if (returnType?.SpecialType is not (null or SpecialType.System_Void))
                {
                    return (possiblyValueReturningStatement.Expression, returnType);
                }
            }
        }
        return null;
    }

    private ScriptGlobals CreateGlobalsObject(string[]? args)
        => new(console, args ?? []);
}