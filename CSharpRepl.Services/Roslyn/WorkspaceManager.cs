// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using CSharpRepl.Services.Extensions;
using CSharpRepl.Services.Logging;
using CSharpRepl.Services.Roslyn.References;
using CSharpRepl.Services.Roslyn.Scripting;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Host.Mef;
using Microsoft.CodeAnalysis.Text;
using System;
using System.Collections.Generic;
using System.Linq;

namespace CSharpRepl.Services.Roslyn;

/// <summary>
/// Editor services like code completion and syntax highlighting require the roslyn workspace/project/document model.
/// Evaluated script code becomes a document in a project, and then each subsequent evaluation adds a new project and
/// document. This new project has a project reference back to the previous project.
/// 
/// In this way, the list of REPL submissions is a linked list of projects, where each project has a single document
/// containing the REPL submission.
/// </summary>
internal sealed class WorkspaceManager
{
    private readonly AdhocWorkspace workspace;

    private readonly CSharpCompilationOptions compilationOptions;
    private readonly AssemblyReferenceService referenceAssemblyService;
    private readonly ITraceLogger logger;

    public Document CurrentDocument { get; private set; }

    public WorkspaceManager(CSharpCompilationOptions compilationOptions, AssemblyReferenceService referenceAssemblyService, ITraceLogger logger)
    {
        this.compilationOptions = compilationOptions;
        this.referenceAssemblyService = referenceAssemblyService;
        this.logger = logger;
        this.workspace = new AdhocWorkspace(MefHostServices.Create(MefHostServices.DefaultAssemblies));

        logger.Log(() => "MEF Default Assemblies: " + string.Join(", ", MefHostServices.DefaultAssemblies.Select(a => a.Location)));

        var assemblyReferences = referenceAssemblyService.EnsureReferenceAssemblyWithDocumentation(referenceAssemblyService.LoadedReferenceAssemblies);

        var document = EmptyProjectAndDocumentChangeset(
                workspace.CurrentSolution,
                assemblyReferences,
                compilationOptions,
                out var documentId
            )
            .ApplyChanges(workspace)
            .GetDocument(documentId);

        if (document is null)
        {
            logger.Log(() =>
                "Null document detected during initialization. Project MetadataReferences: "
                + string.Join(", ", assemblyReferences.Select(r => r.Display))
            );

            throw new InvalidOperationException(RoslynWorkspaceErrorFormat);
        }

        this.CurrentDocument = document;
    }

    public void UpdateCurrentDocument(EvaluationResult.Success result)
    {
        var assemblyReferences = referenceAssemblyService.EnsureReferenceAssemblyWithDocumentation(result.References);
        var document = EmptyProjectAndDocumentChangeset(
                workspace.CurrentSolution,
                assemblyReferences,
                compilationOptions,
                out var documentId
            )
            .WithDocumentText(CurrentDocument.Id, SourceText.From(result.Input))
            .ApplyChanges(workspace)
            .GetDocument(documentId);

        if (document is null)
        {
            logger.Log(() =>
                "Null document detected during update. Project MetadataReferences: "
                + string.Join(", ", assemblyReferences.Select(r => r.Display))
            );
            throw new InvalidOperationException(RoslynWorkspaceErrorFormat);
        }

        this.CurrentDocument = document;
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

    private static DocumentInfo CreateDocument(ProjectInfo projectInfo, string text) =>
        DocumentInfo.Create(
            id: DocumentId.CreateNewId(projectInfo.Id),
            name: projectInfo.Name + "Script",
            sourceCodeKind: SourceCodeKind.Script,
            loader: TextLoader.From(TextAndVersion.Create(SourceText.From(text), VersionStamp.Create()))
        );

    private static ProjectInfo CreateProject(Solution solution, IReadOnlyCollection<MetadataReference> references, CompilationOptions compilationOptions) =>
        ProjectInfo
            .Create(
                id: ProjectId.CreateNewId(),
                version: VersionStamp.Create(),
                name: "Project" + DateTime.UtcNow.Ticks,
                assemblyName: "Project" + DateTime.UtcNow.Ticks,
                language: compilationOptions.Language,
                isSubmission: true
            )
            .WithMetadataReferences(references)
            .WithProjectReferences(solution.ProjectIds.TakeLast(1).Select(id => new ProjectReference(id)))
            .WithCompilationOptions(compilationOptions);

    private const string RoslynWorkspaceErrorFormat = @"Could not initialize Roslyn Workspace. Please consider reporting a bug to https://github.com/waf/CSharpRepl, after running ""csharprepl --trace"" to produce a log file in the current directory.";
}
