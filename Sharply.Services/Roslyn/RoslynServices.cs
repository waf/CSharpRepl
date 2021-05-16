using Sharply.Services.SyntaxHighlighting;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Classification;
using Microsoft.CodeAnalysis.Completion;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.QuickInfo;
using Microsoft.CodeAnalysis.Text;
using PrettyPrompt.Highlighting;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Threading;

namespace Sharply.Services.Roslyn
{
    public class RoslynServices
    {
        private readonly Task initialization;
        private readonly SyntaxHighlighter highlighter;
        private ScriptRunner scriptRunner;
        private WorkspaceManager workspaceManager;
        private PrettyPrinter prettyPrinter;

        public RoslynServices(Configuration config)
        {
            this.highlighter = new SyntaxHighlighter(config.Theme);
            this.initialization = Task.Run(() =>
            {
                var referenceService = new ReferenceAssemblyService(config);
                var compilationOptions = new CSharpCompilationOptions(
                    OutputKind.DynamicallyLinkedLibrary,
                    usings: referenceService.DefaultUsings
                );

                this.scriptRunner = new ScriptRunner(compilationOptions, referenceService.DefaultImplementationAssemblies);
                this.workspaceManager = new WorkspaceManager(compilationOptions, referenceService);
                this.prettyPrinter = new PrettyPrinter();
            });
            initialization.ContinueWith(task => Console.Error.WriteLine(task.Exception.Message), TaskContinuationOptions.OnlyOnFaulted);
        }

        public async Task<EvaluationResult> Evaluate(string input, CancellationToken cancellationToken)
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
            var service = CompletionService.GetService(document);
            var completions = await service.GetCompletionsAsync(document, caret).ConfigureAwait(false);
            return completions?.Items
                .Select(item => new CompletionItemWithDescription(item, new Lazy<Task<string>>(async () =>
                {
                    var currentText = await document.GetTextAsync().ConfigureAwait(false);
                    var completedText = currentText.Replace(item.Span, item.DisplayText);
                    var completedDocument = document.WithText(completedText);
                    var infoService = QuickInfoService.GetService(completedDocument);
                    var info = await infoService.GetQuickInfoAsync(completedDocument, item.Span.End).ConfigureAwait(false);
                    return info is null
                        ? ""
                        : string.Join(Environment.NewLine, info.Sections.Select(s => s.Text));
                })))
                .ToArray()
                ??
                Array.Empty<CompletionItemWithDescription>();
        }

        public AnsiColor ToColor(string keyword) =>
            highlighter.GetColor(keyword);

        public async Task<IReadOnlyCollection<HighlightedSpan>> ClassifySyntax(string text)
        {
            if (!initialization.IsCompleted)
                return Array.Empty<HighlightedSpan>();

            var document = workspaceManager.CurrentDocument.WithText(SourceText.From(text));

            var classified = await Classifier.GetClassifiedSpansAsync(document, TextSpan.FromBounds(0, text.Length)).ConfigureAwait(false);
            var highlighted = highlighter.Highlight(classified.ToList());

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
        public void WarmUp() =>
            Task.Run(async () =>
            {
                await initialization.ConfigureAwait(false);
                await Task.WhenAll(
                    Evaluate(@"_ = ""REPL Warmup""", CancellationToken.None),
                    ClassifySyntax(@"_ = ""REPL Warmup"""),
                    Task.WhenAny((await Complete(@"v", 1)).Select(completion => completion.DescriptionProvider.Value))
                ).ConfigureAwait(false);
            });
    }

    public record CompletionItemWithDescription(CompletionItem Item, Lazy<Task<string>> DescriptionProvider);
}
