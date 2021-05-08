using ReplDotNet.SyntaxHighlighting;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Classification;
using Microsoft.CodeAnalysis.Completion;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.QuickInfo;
using Microsoft.CodeAnalysis.Text;
using PrettyPrompt.Highlighting;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;

namespace ReplDotNet.Roslyn
{
    class RoslynServices
    {
        private readonly Task initialization;
        private readonly SyntaxHighlighter highlighter;
        private ScriptRunner scriptRunner;
        private WorkspaceManager workspaceManager;

        public RoslynServices()
        {
            this.highlighter = new SyntaxHighlighter(null);
            this.initialization = Task.Run(() =>
            {
                var referenceService = new ReferenceAssemblyService();
                var compilationOptions = new CSharpCompilationOptions(
                    OutputKind.DynamicallyLinkedLibrary,
                    usings: referenceService.DefaultUsings
                );

                this.scriptRunner = new ScriptRunner(compilationOptions, referenceService.DefaultImplementationAssemblies);
                this.workspaceManager = new WorkspaceManager(compilationOptions, referenceService);
            });
            initialization.ContinueWith(task => Console.Error.WriteLine(task.Exception.Message), TaskContinuationOptions.OnlyOnFaulted);
        }

        public async Task<EvaluationResult> Evaluate(TextInput input)
        {
            await initialization.ConfigureAwait(false);

            var result = await scriptRunner.RunCompilation(input.Text.Trim()).ConfigureAwait(false);

            if(result is EvaluationResult.Success success)
            {
                // update our final document text, and add a new, empty project that can be
                // used for future evaluations (whether evaluation, syntax highlighting, or completion)
                workspaceManager.UpdateCurrentDocument(success);
            }

            return result;
        }

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

        internal AnsiColor ToColor(string keyword) =>
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

        public void WarmUp() =>
            Task.Run(async () =>
            {
                await initialization.ConfigureAwait(false);
                await Task.WhenAll(
                    Evaluate(new TextInput(@"""REPL Warmup""")),
                    ClassifySyntax(@"""REPL Warmup"""),
                    Complete(@"""REPL Warmup"".", 14)
                ).ConfigureAwait(false);
            });
    }

    record CompletionItemWithDescription(CompletionItem Item, Lazy<Task<string>> DescriptionProvider);

    [DebuggerDisplay("{" + nameof(Text) + "(),nq}")]
    public class TextInput
    {
        public TextInput(string text)
        {
            Text = text;
        }

        public string Text { get; set; }
    }
}
