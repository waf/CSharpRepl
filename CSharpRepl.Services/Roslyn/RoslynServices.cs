// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CSharpRepl.Services.Completion;
using CSharpRepl.Services.Disassembly;
using CSharpRepl.Services.Logging;
using CSharpRepl.Services.Roslyn.References;
using CSharpRepl.Services.Roslyn.Scripting;
using CSharpRepl.Services.SymbolExploration;
using CSharpRepl.Services.SyntaxHighlighting;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Completion;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using Microsoft.Extensions.Caching.Memory;
using PrettyPrompt;
using PrettyPrompt.Consoles;
using PrettyPrompt.Highlighting;
using PrettyPromptOverloadItem = PrettyPrompt.Completion.OverloadItem;
using PrettyPromptTextSpan = PrettyPrompt.Documents.TextSpan;

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
    private readonly IPromptCallbacks defaultPromptCallbacks = new PromptCallbacks();
    private readonly ThreadLocal<OverloadItemGenerator> overloadItemGenerator;
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
        this.overloadItemGenerator = new(() => new(highlighter));

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
            this.scriptRunner = new ScriptRunner(compilationOptions, referenceService, console, config);
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
                workspaceManager.UpdateCurrentDocument(success);
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

    public async Task<PrettyPromptTextSpan> GetSpanToReplaceByCompletionAsync(string text, int caret, CancellationToken cancellationToken)
    {
        await Initialization.ConfigureAwait(false);

        var sourceText = SourceText.From(text);
        var document = workspaceManager.CurrentDocument.WithText(sourceText);
        var completionService = CompletionService.GetService(document);
        if (completionService is null)
        {
            //fallback to default PrettyPrompt implementation
            return await defaultPromptCallbacks.GetSpanToReplaceByCompletionAsync(text, caret, cancellationToken);
        }

        var span = completionService.GetDefaultCompletionListSpan(sourceText, caret);
        return new PrettyPromptTextSpan(span.Start, span.Length);
    }

    public async Task<bool> ShouldOpenCompletionWindowAsync(string text, int caret, KeyPress keyPress, CancellationToken cancellationToken)
    {
        var keyChar = keyPress.ConsoleKeyInfo.KeyChar;
        var keyModifiers = keyPress.ConsoleKeyInfo.Modifiers;
        if (keyChar is '\0' or ' ' or '{' or '(' or '[' or '<' or ':' or '"' ||
            (keyModifiers & ConsoleModifiers.Control) != 0 ||
            (keyModifiers & ConsoleModifiers.Alt) != 0)
        {
            return false;
        }

        await Initialization.ConfigureAwait(false);

        var sourceText = SourceText.From(text);
        var document = workspaceManager.CurrentDocument.WithText(sourceText);
        var completionService = CompletionService.GetService(document);
        if (completionService is null)
        {
            //fallback to default PrettyPrompt implementation
            return await defaultPromptCallbacks.ShouldOpenCompletionWindowAsync(text, caret, keyPress, cancellationToken);
        }

        var trigger = CompletionTrigger.CreateInsertionTrigger(keyChar);
        return completionService.ShouldTriggerCompletion(sourceText, caret, trigger);
    }

    public async Task<bool> ConfirmCompletionCommit(string text, int caret, KeyPress keyPress, CancellationToken cancellationToken)
    {
        var keyChar = keyPress.ConsoleKeyInfo.KeyChar;

        if (keyChar == ' ')
        {
            await Initialization.ConfigureAwait(false);

            var sourceText = SourceText.From(text);
            var document = workspaceManager.CurrentDocument.WithText(sourceText);
            var tree = await document.GetSyntaxTreeAsync(cancellationToken).ConfigureAwait(false);
            if (tree is null) return true;
            var root = await tree.GetRootAsync(cancellationToken).ConfigureAwait(false);

            var node = FindNonWhitespaceNode(text, root, caret);

            if (node is ArgumentSyntax)
            {
                //https://github.com/waf/CSharpRepl/issues/145
                return false;
            }

            if (node is AnonymousObjectMemberDeclaratorSyntax)
            {
                //https://github.com/waf/CSharpRepl/issues/157
                return false;
            }
        }

        return true;
    }

    public async Task<(IReadOnlyList<PrettyPromptOverloadItem> Overloads, int ArgumentIndex)> GetOverloadsAsync(string text, int caret, CancellationToken cancellationToken)
    {
        if (caret > 0)
        {
            await Initialization.ConfigureAwait(false);

            var sourceText = SourceText.From(text);
            var document = workspaceManager.CurrentDocument.WithText(sourceText);

            var tree = await document.GetSyntaxTreeAsync(cancellationToken);
            if (tree is null) return Empty();

            var root = await tree.GetRootAsync(cancellationToken);

            var node = FindNonWhitespaceNode(text, root, caret);
            if (node is null) return Empty();

            while (!node.IsKind(SyntaxKind.ArgumentList) && !node.IsKind(SyntaxKind.BracketedArgumentList))
            {
                node = node.Parent;
                if (node is null) return Empty();
            }

            return await GetOverloadsForArgList(document, (BaseArgumentListSyntax)node);
        }

        return Empty();

        static (IReadOnlyList<PrettyPromptOverloadItem> Overloads, int ArgumentIndex) Empty() => (Array.Empty<PrettyPromptOverloadItem>(), 0);

        async Task<(IReadOnlyList<PrettyPromptOverloadItem> Overloads, int ArgumentIndex)> GetOverloadsForArgList(Document document, BaseArgumentListSyntax argList)
        {
            var argListSpan = argList.GetLocation().SourceSpan;
            if (caret <= argListSpan.Start)
            {
                //we are before opening parenthesis of arg list

                if (TryGetArgListParent(argList.Parent, out var parentArgList))
                {
                    //we could be nested in multiple arg lists
                    return await GetOverloadsForArgList(document, parentArgList);
                }

                return Empty();
            }

            var closeParenToken = (argList as ArgumentListSyntax)?.CloseParenToken ?? (argList as BracketedArgumentListSyntax)?.CloseBracketToken;
            if (closeParenToken?.Span.Length > 0 && caret >= argListSpan.End)
            {
                //we are after closing parenthesis of arg list

                if (TryGetArgListParent(argList.Parent, out var parentArgList))
                {
                    //we could be nested in multiple arg lists
                    return await GetOverloadsForArgList(document, parentArgList);
                }

                return Empty();
            }

            var semanticModel = await document.GetSemanticModelAsync(cancellationToken);
            if (semanticModel is null) return Empty();

            var symbols = GetMemberGroup(semanticModel, argList.Parent, cancellationToken);
            if (symbols.Length > 0)
            {
                var items = new List<PrettyPromptOverloadItem>(symbols.Length);
                for (int i = 0; i < symbols.Length; i++)
                {
                    if (symbols[i] is IMethodSymbol method &&
                        overloadItemGenerator.Value!.Create(method, cancellationToken) is { } methodItem)
                    {
                        items.Add(methodItem);
                    }
                    else if (symbols[i] is IPropertySymbol property &&
                        overloadItemGenerator.Value!.Create(property, cancellationToken) is { } propertyItem)
                    {
                        items.Add(propertyItem);
                    }
                }

                int argIndex = 0;
                foreach (var separator in argList.Arguments.GetSeparators())
                {
                    if (caret <= separator.SpanStart)
                    {
                        break;
                    }
                    ++argIndex;
                }

                return (items, argIndex);
            }
            else
            {
                return Empty();
            }
        }

        static bool TryGetArgListParent(SyntaxNode? node, [NotNullWhen(true)] out ArgumentListSyntax? result)
        {
            while (node != null)
            {
                if (node is ArgumentListSyntax argList)
                {
                    result = argList;
                    return true;
                }
                node = node.Parent;
            }
            result = null;
            return false;
        }

        static ImmutableArray<ISymbol> GetMemberGroup(SemanticModel semanticModel, SyntaxNode? node, CancellationToken cancellationToken)
        {
            if (node is InvocationExpressionSyntax invocationExpression)
            {
                return semanticModel.GetMemberGroup(invocationExpression.Expression, cancellationToken);
            }
            else if (node is ObjectCreationExpressionSyntax objectCreationExpression)
            {
                return semanticModel.GetMemberGroup(objectCreationExpression, cancellationToken);
            }
            else if (node is ElementAccessExpressionSyntax elementAccessExpression)
            {
                return semanticModel.GetIndexerGroup(elementAccessExpression.Expression, cancellationToken).Cast<ISymbol>().ToImmutableArray();
            }
            else if (node is ConstructorInitializerSyntax constructorInitializer)
            {
                //TODO - this does not work because (i think this from debugging GetMemberGroup) it looks for oveloads of the 'caller ctor'
                //       we probably need to look for type and depending on if 'constructorInitializer' is 'base' or 'this' we need
                //       to manualy get overloads for base/this from semantic model
                //return semanticModel.GetMemberGroup(constructorInitializer, cancellationToken).Cast<ISymbol>().ToImmutableArray();
            }
            return ImmutableArray<ISymbol>.Empty;
        }
    }

    private SyntaxNode? FindNonWhitespaceNode(string text, SyntaxNode root, int caret)
    {
        var node = root.FindNode(new TextSpan(caret, 0));
        if (node.IsKind(SyntaxKind.CompilationUnit))
        {
            for (int i = caret - 1; i >= 0; i--)
            {
                if (!char.IsWhiteSpace(text[i]))
                {
                    return root.FindNode(new TextSpan(i, 0));
                }
            }
        }
        return node;
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
