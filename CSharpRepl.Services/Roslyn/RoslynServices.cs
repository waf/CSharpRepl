// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using CSharpRepl.Services.SyntaxHighlighting;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using PrettyPrompt.Highlighting;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Threading;
using CSharpRepl.Services.SymbolExploration;
using CSharpRepl.Services.Completion;
using Microsoft.Extensions.Caching.Memory;
using PrettyPrompt.Consoles;

namespace CSharpRepl.Services.Roslyn
{
    public class RoslynServices
    {
        private readonly Task initialization;
        private readonly SyntaxHighlighter highlighter;
        private ScriptRunner scriptRunner;
        private WorkspaceManager workspaceManager;
        private PrettyPrinter prettyPrinter;
        private SymbolExplorer symbolExplorer;
        private AutoCompleteService autocompleteService;

        public RoslynServices(IConsole console, Configuration config)
        {
            var cache = new MemoryCache(new MemoryCacheOptions());
            this.highlighter = new SyntaxHighlighter(cache, config.Theme);
            // initialization of roslyn and all dependent services is slow! do it asynchronously so we don't increase startup time.
            this.initialization = Task.Run(() =>
            {
                var referenceService = new ReferenceAssemblyService(config);

                var compilationOptions = new CSharpCompilationOptions(
                    OutputKind.DynamicallyLinkedLibrary,
                    usings: referenceService.DefaultUsings,
                    allowUnsafe: true 
                );

                // the script runner is used to actually execute the scripts, and the workspace manager
                // is updated alongside. The workspace is a datamodel used in "editor services" like
                // syntax highlighting, autocompletion, and roslyn symbol queries.
                this.scriptRunner = new ScriptRunner(console, compilationOptions, referenceService);
                this.workspaceManager = new WorkspaceManager(compilationOptions, referenceService);

                this.prettyPrinter = new PrettyPrinter();
                this.symbolExplorer = new SymbolExplorer();
                this.autocompleteService = new AutoCompleteService(cache);
            });
            initialization.ContinueWith(task => console.WriteErrorLine(task.Exception.Message), TaskContinuationOptions.OnlyOnFaulted);
        }

        public async Task<EvaluationResult> Evaluate(string input, CancellationToken cancellationToken = default)
        {
            await initialization.ConfigureAwait(false);

            var result = await scriptRunner
                .RunCompilation(input.Trim(), cancellationToken)
                .ConfigureAwait(false);

            if (result is EvaluationResult.Success success)
            {
                // update our final document text, and add a new, empty project that can be
                // used for future evaluations (whether evaluation, syntax highlighting, or completion)
                workspaceManager.UpdateCurrentDocument(success);
            }

            return result;
        }

        public string PrettyPrint(object obj, bool displayDetails) =>
            obj is Exception ex
            ? prettyPrinter.FormatException(ex, displayDetails)
            : prettyPrinter.FormatObject(obj, displayDetails);

        public async Task<IReadOnlyCollection<CompletionItemWithDescription>> Complete(string text, int caret)
        {
            if (!initialization.IsCompleted)
                return Array.Empty<CompletionItemWithDescription>();

            var document = workspaceManager.CurrentDocument.WithText(SourceText.From(text));
            return await autocompleteService.Complete(document, text, caret).ConfigureAwait(false);
        }

        public async Task<SymbolResult> GetSymbolAtIndex(string text, int caret)
        {
            await initialization.ConfigureAwait(false);
            var document = workspaceManager.CurrentDocument.WithText(SourceText.From(text));
            return await symbolExplorer.GetSymbolAtPosition(document, caret);
        }

        public AnsiColor ToColor(string keyword) =>
            highlighter.GetColor(keyword);

        public async Task<IReadOnlyCollection<HighlightedSpan>> SyntaxHighlightAsync(string text)
        {
            if (!initialization.IsCompleted)
                return Array.Empty<HighlightedSpan>();

            var document = workspaceManager.CurrentDocument.WithText(SourceText.From(text));
            var highlighted = await highlighter.HighlightAsync(document);

            return highlighted;
        }

        public async Task<bool> IsTextCompleteStatement(string text)
        {
            if (!initialization.IsCompleted)
                return true;

            var document = workspaceManager.CurrentDocument.WithText(SourceText.From(text));
            var root = await document.GetSyntaxRootAsync().ConfigureAwait(false);
            return SyntaxFactory.IsCompleteSubmission(root.SyntaxTree);
        }

        /// <summary>
        /// Roslyn services can be a bit slow to initialize the first time they're executed.
        /// Warm them up in the background so it doesn't affect the user.
        /// </summary>
        public Task WarmUpAsync() =>
            Task.Run(async () =>
            {
                await initialization.ConfigureAwait(false);
                await Task.WhenAll(
                    Evaluate(@"_ = ""REPL Warmup""", CancellationToken.None),
                    SyntaxHighlightAsync(@"_ = ""REPL Warmup"""),
                    Task.WhenAny((await Complete(@"C", 1)).Select(completion => completion.DescriptionProvider.Value))
                ).ConfigureAwait(false);
            });
    }
}
