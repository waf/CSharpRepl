using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Classification;
using Microsoft.CodeAnalysis.Completion;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Host.Mef;
using Microsoft.CodeAnalysis.Scripting;
using Microsoft.CodeAnalysis.Text;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace CSharpLangRepl
{
    class ScriptEvaluator
    {
        private readonly AdhocWorkspace workspace;
        private Document activeDocument;
        private readonly MetadataReference[] defaultReferences;
        private readonly CSharpCompilationOptions compilationOptions;
        public CompilationRunner compilationRunner;

        public ScriptEvaluator()
        {
            defaultReferences =
                AppDomain.CurrentDomain.GetAssemblies()
                .Where(assembly => !assembly.IsDynamic && !string.IsNullOrWhiteSpace(assembly.Location))
                .Select(assembly => assembly.Location)
                .Concat(new[] { typeof(HttpClient).Assembly.Location })
                .Distinct()
                .Select(assembly => MetadataReference.CreateFromFile(assembly))
                .ToArray();

            compilationOptions = new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, usings: new[]
            {
                "System", "System.IO", "System.Collections.Generic", "System.Console", "System.Diagnostics",
                "System.Linq", "System.Net.Http", "System.Text",
                "System.Threading.Tasks"
            });

            var scriptOptions = ScriptOptions.Default.AddReferences(defaultReferences).AddImports(compilationOptions.Usings);
            compilationRunner = new CompilationRunner(scriptOptions);

            var host = MefHostServices.Create(MefHostServices.DefaultAssemblies);
            workspace = new AdhocWorkspace(host);
            activeDocument = EmptyProjectAndDocumentChangeset(
                    workspace.CurrentSolution,
                    defaultReferences,
                    compilationOptions,
                    out var documentId
                )
                .ApplyChanges(workspace)
                .GetDocument(documentId);
        }

        public async Task<EvaluationResult> Evaluate(TextInput input)
        {
            var result = await compilationRunner.RunCompilation(input.Text);
            if(result is EvaluationResult.Error)
            {
                return result;
            }

            // update our final document text, and add a new, empty project that can be
            // used for future evaluations (whether evaluation, syntax highlighting, or completion)
            activeDocument = EmptyProjectAndDocumentChangeset(
                    workspace.CurrentSolution,
                    defaultReferences,
                    compilationOptions,
                    out var documentId
                )
                .WithDocumentText(activeDocument.Id, SourceText.From(input.Text))
                .ApplyChanges(workspace)
                .GetDocument(documentId);

            return result;
        }


        private static DocumentInfo CreateDocument(ProjectInfo projectInfo, string text)
        {
            return DocumentInfo.Create(
                id: DocumentId.CreateNewId(projectInfo.Id),
                name: projectInfo.Name + "Script",
                sourceCodeKind: SourceCodeKind.Script,
                loader: TextLoader.From(TextAndVersion.Create(SourceText.From(text), VersionStamp.Create()))
            );
        }

        private static ProjectInfo CreateProject(Solution solution, IReadOnlyCollection<MetadataReference> references, CompilationOptions compilationOptions)
        {
            var projectId = ProjectId.CreateNewId();
            var projectInfo = ProjectInfo
                .Create(
                    id: projectId,
                    version: VersionStamp.Create(),
                    name: "Project" + DateTime.UtcNow.Ticks,
                    assemblyName: "Project" + DateTime.UtcNow.Ticks,
                    language: compilationOptions.Language,
                    isSubmission: true
                )
                .WithMetadataReferences(references)
                .WithProjectReferences(solution.ProjectIds.TakeLast(1).Select(id => new ProjectReference(id)))
                .WithCompilationOptions(compilationOptions);
            return projectInfo;
        }

        private static Solution EmptyProjectAndDocumentChangeset(
            Solution solution,
            IReadOnlyCollection<MetadataReference> references,
            CompilationOptions compilationOptions,
            out DocumentId documentId)
        {
            var projectInfo = CreateProject(solution, references, compilationOptions);
            var documentInfo = CreateDocument(projectInfo, string.Empty);

            documentId = documentInfo.Id;

            return solution
                .AddProject(projectInfo)
                .AddDocument(documentInfo);
        }

        public async Task<CompletionList> Complete(string text, int caret)
        {
            var document = activeDocument.WithText(SourceText.From(text));
            var service = CompletionService.GetService(document);
            var completions = await service.GetCompletionsAsync(document, caret);
            return completions;
        }

        public async Task<IReadOnlyCollection<ClassifiedSpan>> Highlight(string text)
        {
            var document = activeDocument.WithText(SourceText.From(text));
            IEnumerable<ClassifiedSpan> classified = await Classifier.GetClassifiedSpansAsync(
                document,
                TextSpan.FromBounds(0, text.Length)
            );
            return classified.ToList();
        }
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
