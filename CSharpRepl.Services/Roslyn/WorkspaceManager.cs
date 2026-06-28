// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CSharpRepl.Services.Extensions;
using CSharpRepl.Services.Logging;
using CSharpRepl.Services.Roslyn.References;
using CSharpRepl.Services.Roslyn.Scripting;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Host;
using Microsoft.CodeAnalysis.Host.Mef;
using Microsoft.CodeAnalysis.Text;

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

    /// <summary>
    /// The script globals type whose members are in scope for every submission (e.g. <c>services</c> /
    /// <c>Get&lt;T&gt;()</c> from the connector's globals). Null for the local REPL, which doesn't surface its
    /// globals in completion. When set, it's applied as each submission project's host object type, so editor
    /// services resolve the globals members. The type's assembly must be among the project's references.
    /// </summary>
    private readonly Type? hostObjectType;

    public Document CurrentDocument { get; private set; }

    public WorkspaceManager(CSharpCompilationOptions compilationOptions, AssemblyReferenceService referenceAssemblyService, ITraceLogger logger, Type? hostObjectType = null, HostServices? hostServices = null)
    {
        this.compilationOptions = compilationOptions;
        this.referenceAssemblyService = referenceAssemblyService;
        this.logger = logger;
        this.hostObjectType = hostObjectType;
        this.workspace = new AdhocWorkspace(hostServices ?? MefHostServices.DefaultHost);

        logger.Log(() => "MEF Default Assemblies: " + string.Join(", ", MefHostServices.DefaultAssemblies.Select(a => a.Location)));

        var assemblyReferences = referenceAssemblyService.EnsureReferenceAssemblyWithDocumentation(referenceAssemblyService.LoadedReferenceAssemblies);

        var document = EmptyProjectAndDocumentChangeset(
                workspace.CurrentSolution,
                assemblyReferences,
                compilationOptions,
                hostObjectType,
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

    public async Task UpdateCurrentDocumentAsync(EvaluationResult.Success result, CancellationToken cancellationToken = default)
    {
        var assemblyReferences = referenceAssemblyService.EnsureReferenceAssemblyWithDocumentation(result.References);
        var document = EmptyProjectAndDocumentChangeset(
                workspace.CurrentSolution,
                assemblyReferences,
                compilationOptions,
                hostObjectType,
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

        // Performance:
        // Generate this snapshot's compilation now by calling GetCompilationAsync. Roslyn holds a project's Compilation once its CompilationTracker reaches the final state (FinalCompilationWithGeneratedDocuments):
        // https://github.com/dotnet/roslyn/blob/9fa44037cfccdd8ec2e56429627522e15712af23/src/Workspaces/Core/Portable/Workspace/Solution/SolutionCompilationState.CompilationTracker.CompilationTrackerState.cs#L162
        // Each per-keystroke CurrentDocument.WithText(...) makes a *new* snapshot, but it reuses the previous submission's compilation if it's been generated as above.
        // The result is discarded on purpose. CurrentDocument already references this snapshot for the rest of the session; we only need to generate the compilation, not hold the Compilation ourselves.
        // Confirmed by benchmarks; if we comment out the following line, memory usage grows with each submission, and code completion becomes slower.
        _ = await CurrentDocument.Project.GetCompilationAsync(cancellationToken).ConfigureAwait(false);
    }

    public IReadOnlyList<Document> GetPreviousDocuments()
    {
        var documents = workspace.CurrentSolution.Projects.SelectMany(project => project.Documents).ToList();
        return documents;
    }

    private static Solution EmptyProjectAndDocumentChangeset(
        Solution solution,
        IReadOnlyCollection<MetadataReference> references,
        CompilationOptions compilationOptions,
        Type? hostObjectType,
        out DocumentId documentId)
    {
        var projectInfo = CreateProject(solution, references, compilationOptions, hostObjectType);
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

    private static ProjectInfo CreateProject(Solution solution, IReadOnlyCollection<MetadataReference> references, CompilationOptions compilationOptions, Type? hostObjectType) =>
        ProjectInfo
            .Create(
                id: ProjectId.CreateNewId(),
                version: VersionStamp.Create(),
                name: "Project" + DateTime.UtcNow.Ticks,
                assemblyName: "Project" + DateTime.UtcNow.Ticks,
                language: compilationOptions.Language,
                isSubmission: true,
                hostObjectType: hostObjectType
            )
            .WithMetadataReferences(references)
            .WithProjectReferences(solution.ProjectIds.TakeLast(1).Select(id => new ProjectReference(id)))
            .WithCompilationOptions(compilationOptions);

    private const string RoslynWorkspaceErrorFormat = @"Could not initialize Roslyn Workspace. Please consider reporting a bug to https://github.com/waf/CSharpRepl, after running ""csharprepl --trace"" to produce a log file in the current directory.";
}
