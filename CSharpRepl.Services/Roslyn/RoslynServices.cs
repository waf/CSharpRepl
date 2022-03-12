// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading.Tasks;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using Microsoft.Extensions.Caching.Memory;
using PrettyPrompt.Consoles;
using PrettyPrompt.Highlighting;
using CSharpRepl.Services.SymbolExploration;
using CSharpRepl.Services.Completion;
using CSharpRepl.Services.SyntaxHighlighting;
using CSharpRepl.Services.Roslyn.References;
using CSharpRepl.Services.Roslyn.Scripting;
using CSharpRepl.Services.Disassembly;
using CSharpRepl.Services.Logging;

namespace CSharpRepl.Services.Roslyn;

/// <summary>
/// The main entry point of all services. This is a facade for other services that manages their startup and initialization.
/// It also ensures two different areas of the Roslyn API, the Scripting and Workspace APIs, remain in sync.
/// </summary>
public sealed class RoslynServices
{
    private readonly SyntaxHighlighter highlighter;
    private readonly ITraceLogger logger;
    private readonly SemaphoreSlim semaphore = new(1);
    private ScriptRunner? scriptRunner;
    private WorkspaceManager? workspaceManager;
    private Disassembler? disassembler;
    private PrettyPrinter? prettyPrinter;
    private SymbolExplorer? symbolExplorer;
    private AutoCompleteService? autocompleteService;
    private AssemblyReferenceService? referenceService;
    private CSharpCompilationOptions? compilationOptions;

    // when this Initialization task successfully completes, all the above members will not be null.
    [MemberNotNull(
        nameof(scriptRunner), nameof(workspaceManager), nameof(disassembler),
        nameof(prettyPrinter), nameof(symbolExplorer), nameof(autocompleteService),
        nameof(referenceService), nameof(compilationOptions))]
    private Task Initialization { get; }

    public RoslynServices(IConsole console, Configuration config, ITraceLogger logger)
    {
        var cache = new MemoryCache(new MemoryCacheOptions());
        this.logger = logger;
        this.highlighter = new SyntaxHighlighter(cache, config.Theme);
        // initialization of roslyn and all dependent services is slow! do it asynchronously so we don't increase startup time.
        this.Initialization = Task.Run(() =>
        {
            logger.Log("Starting background initialization");
            this.referenceService = new AssemblyReferenceService(config, logger);

            this.compilationOptions = new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                usings: referenceService.Usings.Select(u => u.Name.ToString()),
                allowUnsafe: true
            );

            // the script runner is used to actually execute the scripts, and the workspace manager
            // is updated alongside. The workspace is a datamodel used in "editor services" like
            // syntax highlighting, autocompletion, and roslyn symbol queries.
            this.scriptRunner = new ScriptRunner(compilationOptions, referenceService, console);
            this.workspaceManager = new WorkspaceManager(compilationOptions, referenceService, logger);

                this.disassembler = new Disassembler(compilationOptions, referenceService, scriptRunner);
                this.prettyPrinter = new PrettyPrinter();
                this.symbolExplorer = new SymbolExplorer(referenceService, scriptRunner);
                this.autocompleteService = new AutoCompleteService(highlighter, cache, config);
                logger.Log("Background initialization complete");
            });
            Initialization.ContinueWith(task => console.WriteErrorLine(task.Exception?.Message ?? "Unknown error"), TaskContinuationOptions.OnlyOnFaulted);
        }

    public async Task<EvaluationResult> EvaluateAsync(string input, string[]? args = null, CancellationToken cancellationToken = default)
    {
        await Initialization.ConfigureAwait(false);

        try
        {
            //each RunCompilation (modifies script state) and UpdateCurrentDocument (changes CurrentDocument) cannot be run concurrently
            await semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);

            var result = await scriptRunner
                .RunCompilation(input.Trim(), args, cancellationToken)
                .ConfigureAwait(false);

            if (result is EvaluationResult.Success success)
            {
                // update our final document text, and add a new, empty project that can be
                // used for future evaluations (whether evaluation, syntax highlighting, or completion)
                workspaceManager!.UpdateCurrentDocument(success);
            }

            return result;
        }
        finally
        {
            semaphore.Release();
        }
    }

    public async Task<string?> PrettyPrintAsync(object? obj, bool displayDetails)
    {
        await Initialization.ConfigureAwait(false);
        return obj is Exception ex
            ? prettyPrinter.FormatException(ex, displayDetails)
            : prettyPrinter.FormatObject(obj, displayDetails);
    }

    public async Task<IReadOnlyCollection<CompletionItemWithDescription>> CompleteAsync(string text, int caret)
    {
        if (!Initialization.IsCompleted)
            return Array.Empty<CompletionItemWithDescription>();

        var document = workspaceManager.CurrentDocument.WithText(SourceText.From(text));
        return await autocompleteService.Complete(document, text, caret).ConfigureAwait(false);
    }

    public async Task<SymbolResult> GetSymbolAtIndexAsync(string text, int caret)
    {
        await Initialization.ConfigureAwait(false);
        return await symbolExplorer.LookupSymbolAtPosition(text, caret);
    }

    public AnsiColor ToColor(string keyword) =>
        highlighter.GetColor(keyword);

    public async Task<IReadOnlyCollection<HighlightedSpan>> SyntaxHighlightAsync(string text)
    {
        if (!Initialization.IsCompleted)
            return Array.Empty<HighlightedSpan>();

        var document = workspaceManager.CurrentDocument.WithText(SourceText.From(text));
        var highlighted = await highlighter.HighlightAsync(document);

        return highlighted;
    }

    public async Task<bool> IsTextCompleteStatementAsync(string text)
    {
        if (!Initialization.IsCompleted)
            return true;

        var document = workspaceManager.CurrentDocument.WithText(SourceText.From(text));
        var root = await document.GetSyntaxRootAsync().ConfigureAwait(false);
        return root is null || SyntaxFactory.IsCompleteSubmission(root.SyntaxTree); // if something's wrong and we can't get the syntax tree, we don't want to prevent evaluation.
    }

    public async Task<EvaluationResult> ConvertToIntermediateLanguage(string csharpCode, bool debugMode)
    {
        await Initialization.ConfigureAwait(false);
        return disassembler.Disassemble(csharpCode, debugMode);
    }

    /// <summary>
    /// Roslyn services can be a bit slow to initialize the first time they're executed.
    /// Warm them up in the background so it doesn't affect the user.
    /// </summary>
    public Task WarmUpAsync(string[] args) =>
        Task.Run(async () =>
        {
            await Initialization.ConfigureAwait(false);

            logger.Log("Warm-up Starting");

            var evaluationTask = EvaluateAsync(@"_ = ""REPL Warmup""", args);
            var highlightTask = SyntaxHighlightAsync(@"_ = ""REPL Warmup""");
            var completionTask = Task.WhenAny(
                (await CompleteAsync(@"C", 1))
                    .Where(completion => completion.Item.DisplayText.StartsWith("C"))
                    .Take(15)
                    .Select(completion => completion.GetDescriptionAsync(cancellationToken: default))
            );

            await Task.WhenAll(evaluationTask, highlightTask, completionTask).ConfigureAwait(false);
            logger.Log("Warm-up Complete");
        });
}
