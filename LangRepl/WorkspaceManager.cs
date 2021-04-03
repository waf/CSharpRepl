using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Host.Mef;
using Microsoft.CodeAnalysis.Text;
using System;
using System.Collections.Generic;
using System.Linq;

namespace LangRepl
{
    class WorkspaceManager
    {
        private readonly AdhocWorkspace workspace;
        private readonly CSharpCompilationOptions compilationOptions;
        private readonly MetadataReference[] defaultReferences;

        public Document CurrentDocument { get; private set; }

        public WorkspaceManager(CSharpCompilationOptions compilationOptions, MetadataReference[] defaultReferences)
        {
            this.compilationOptions = compilationOptions;
            this.defaultReferences = defaultReferences;
            this.workspace = new AdhocWorkspace(MefHostServices.Create(MefHostServices.DefaultAssemblies));

            this.CurrentDocument = EmptyProjectAndDocumentChangeset(
                    workspace.CurrentSolution,
                    defaultReferences,
                    compilationOptions,
                    out var documentId
                )
                .ApplyChanges(workspace)
                .GetDocument(documentId);
        }

        public void UpdateCurrentDocument(string text)
        {
            CurrentDocument = EmptyProjectAndDocumentChangeset(
                    workspace.CurrentSolution,
                    defaultReferences,
                    compilationOptions,
                    out var documentId
                )
                .WithDocumentText(CurrentDocument.Id, SourceText.From(text))
                .ApplyChanges(workspace)
                .GetDocument(documentId);
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

    }
}
