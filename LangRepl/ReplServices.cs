using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Classification;
using Microsoft.CodeAnalysis.Completion;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;

namespace LangRepl
{
    class ReplServices
    {
        private readonly Task initialization;
        private ScriptRunner scriptRunner;
        private WorkspaceManager workspaceManager;

        public ReplServices()
        {
            this.initialization = Task.Run(() =>
            {
                var defaultReferences =
                    AppDomain.CurrentDomain.GetAssemblies()
                    .Where(assembly => !assembly.IsDynamic && !string.IsNullOrWhiteSpace(assembly.Location))
                    .Select(assembly => assembly.Location)
                    .Concat(new[] { typeof(HttpClient).Assembly.Location })
                    .Distinct()
                    .Select(assembly => MetadataReference.CreateFromFile(assembly))
                    .ToArray();

                var compilationOptions = new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, usings: new[]
                {
                    "System", "System.IO", "System.Collections.Generic", "System.Console", "System.Diagnostics",
                    "System.Linq", "System.Net.Http", "System.Text",
                    "System.Threading.Tasks"
                });

                this.scriptRunner = new ScriptRunner(compilationOptions, defaultReferences);
                this.workspaceManager = new WorkspaceManager(compilationOptions, defaultReferences);
            });
        }

        public async Task<EvaluationResult> Evaluate(TextInput input)
        {
            await initialization.ConfigureAwait(false);

            var result = await scriptRunner.RunCompilation(input.Text).ConfigureAwait(false);
            if(result is EvaluationResult.Error)
            {
                return result;
            }

            // update our final document text, and add a new, empty project that can be
            // used for future evaluations (whether evaluation, syntax highlighting, or completion)
            workspaceManager.UpdateCurrentDocument(input.Text);

            return result;
        }

        public async Task<CompletionList> Complete(string text, int caret)
        {
            if (!initialization.IsCompleted)
                return CompletionList.Empty;

            var document = workspaceManager.CurrentDocument.WithText(SourceText.From(text));
            var service = CompletionService.GetService(document);
            var completions = await service.GetCompletionsAsync(document, caret).ConfigureAwait(false);
            return completions;
        }

        public async Task<IReadOnlyCollection<ClassifiedSpan>> Highlight(string text)
        {
            if (!initialization.IsCompleted)
                return Array.Empty<ClassifiedSpan>();

            var document = workspaceManager.CurrentDocument.WithText(SourceText.From(text));
            IEnumerable<ClassifiedSpan> classified = await Classifier.GetClassifiedSpansAsync(
                document,
                TextSpan.FromBounds(0, text.Length)
            ).ConfigureAwait(false);
            return classified.ToList();
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
                    Evaluate(new TextInput(@"""Hello World""")),
                    Highlight(@"""Hello World"""),
                    Complete(@"""Hello World"".", 14)
                ).ConfigureAwait(false);
            });
    }

    public class TextInput
    {
        public TextInput(string text)
        {
            Text = text;
        }

        public string Text { get; set; }
    }
    public class SyntaxTreeInput : TextInput
    {
        public SyntaxTreeInput(TextInput textInput, SyntaxTree tree)
            : base(textInput.Text)
        {
            this.SyntaxTree = tree;
        }

        public SyntaxTree SyntaxTree { get; }
    }
}
