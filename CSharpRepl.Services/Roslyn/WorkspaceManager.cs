// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Host.Mef;
using Microsoft.CodeAnalysis.Text;
using System;
using System.Collections.Generic;
using System.Linq;

namespace CSharpRepl.Services.Roslyn
{
    /// <summary>
    /// Editor services like code completion and syntax highlighting require the roslyn workspace/project/document model.
    /// Evaluated script code becomes a document in a project, and then each subsequent evaluation adds a new project and
    /// document. This new project has a project reference back to the previous project.
    /// 
    /// In this way, the list of REPL submissions is a linked list of projects, where each project has a single document
    /// containing the REPL submission.
    /// </summary>
    class WorkspaceManager
    {
        private readonly AdhocWorkspace workspace;
        private readonly CSharpCompilationOptions compilationOptions;
        private readonly ReferenceAssemblyService referenceAssemblyService;

        public Document CurrentDocument { get; private set; }

        public WorkspaceManager(CSharpCompilationOptions compilationOptions, ReferenceAssemblyService referenceAssemblyService)
        {
            this.compilationOptions = compilationOptions;
            this.referenceAssemblyService = referenceAssemblyService;
            this.workspace = new AdhocWorkspace(MefHostServices.Create(MefHostServices.DefaultAssemblies));

            this.CurrentDocument = EmptyProjectAndDocumentChangeset(
                    workspace.CurrentSolution,
                    referenceAssemblyService.EnsureReferenceAssemblyWithDocumentation(referenceAssemblyService.LoadedReferenceAssemblies),
                    compilationOptions,
                    out var documentId
                )
                .ApplyChanges(workspace)
                .GetDocument(documentId);
        }

        public void UpdateCurrentDocument(EvaluationResult.Success result)
        {
            CurrentDocument = EmptyProjectAndDocumentChangeset(
                    workspace.CurrentSolution,
                    referenceAssemblyService.EnsureReferenceAssemblyWithDocumentation(result.References),
                    compilationOptions,
                    out var documentId
                )
                .WithDocumentText(CurrentDocument.Id, SourceText.From(result.Input))
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
