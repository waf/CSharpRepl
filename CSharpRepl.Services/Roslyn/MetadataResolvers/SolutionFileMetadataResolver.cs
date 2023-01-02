// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CSharpRepl.Services.Dotnet;
using Microsoft.Build.Locator;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.MSBuild;

namespace CSharpRepl.Services.Roslyn.MetadataResolvers;

internal sealed class SolutionFileMetadataResolver : AlternativeReferenceResolver
{
    private readonly DotnetBuilder builder;
    private readonly IConsoleEx console;

    public SolutionFileMetadataResolver(DotnetBuilder builder, IConsoleEx console)
    {
        this.builder = builder;
        this.console = console;

        if (!MSBuildLocator.IsRegistered)
        {
            _ = MSBuildLocator.RegisterDefaults();
        }
    }

    public override bool CanResolve(string reference) =>
        reference.EndsWith(".sln", StringComparison.OrdinalIgnoreCase) ||
        reference.EndsWith(".sln\"", StringComparison.OrdinalIgnoreCase) ||
        reference.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase) ||
        reference.EndsWith(".csproj\"", StringComparison.OrdinalIgnoreCase);

    public override async Task<ImmutableArray<PortableExecutableReference>> ResolveAsync(string reference, CancellationToken cancellationToken)
    {
        var solutionPath = Path.GetFullPath(reference
            .Split('\"', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Last());

        var (exitCode, _) = await builder.BuildAsync(solutionPath, cancellationToken);

        if (exitCode != 0)
        {
            console.WriteErrorLine("Reference not added: build failed.");
            return ImmutableArray<PortableExecutableReference>.Empty;
        }

        console.WriteLine("Adding references from built project...");
        var metadataReferences = await GetMetadataReferences(solutionPath, cancellationToken);
        return metadataReferences;
    }

    private async Task<ImmutableArray<PortableExecutableReference>> GetMetadataReferences(string solutionOrProject, CancellationToken cancellationToken)
    {
        var workspace = MSBuildWorkspace.Create();

        var projects = Path.GetExtension(solutionOrProject) switch
        {
            ".csproj" => new[] { await workspace.OpenProjectAsync(solutionOrProject, cancellationToken: cancellationToken) },
            ".sln" => (await workspace.OpenSolutionAsync(solutionOrProject, cancellationToken: cancellationToken)).Projects,
            _ => throw new ArgumentException("Unexpected filetype for file " + solutionOrProject)
        };

        foreach (var error in workspace.Diagnostics.Where(d => d.Kind == WorkspaceDiagnosticKind.Failure))
        {
            console.WriteErrorLine(error.Message);
        }

        return projects
            .SelectMany(p =>
                p.MetadataReferences
                .OfType<PortableExecutableReference>()
                .Concat(p.OutputFilePath is not null
                    ? new[] { MetadataReference.CreateFromFile(p.OutputFilePath) }
                    : Array.Empty<PortableExecutableReference>()
                )
            )
            .Distinct()
            .ToImmutableArray();
    }
}