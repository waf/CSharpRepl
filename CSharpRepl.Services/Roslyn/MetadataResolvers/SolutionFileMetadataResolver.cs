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
using Microsoft.CodeAnalysis;
using PrettyPrompt.Consoles;

namespace CSharpRepl.Services.Roslyn.MetadataResolvers;

internal sealed class SolutionFileMetadataResolver : AlternativeReferenceResolver
{
    private readonly DotnetBuilder builder;
    private readonly IConsole console;

    public SolutionFileMetadataResolver(DotnetBuilder builder, IConsole console)
    {
        this.builder = builder;
        this.console = console;
    }

    public override bool CanResolve(string reference) =>
        reference.EndsWith(".sln", StringComparison.OrdinalIgnoreCase) ||
        reference.EndsWith(".sln\"", StringComparison.OrdinalIgnoreCase);

    public override async Task<ImmutableArray<PortableExecutableReference>> ResolveAsync(string reference, CancellationToken cancellationToken)
    {
        var solutionPath = Path.GetFullPath(reference
            .Split('\"', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Last());

        var (exitCode, output) = await builder.BuildAsync(solutionPath, cancellationToken);

        var projectPaths = builder
            .ParseBuildGraph(output)
            .Select(kvp => Path.GetFullPath(kvp.Value)); //GetFullPath will normalize separators

        if (!projectPaths.Any())
        {
            console.WriteErrorLine("Project reference not added: could not determine built assembly");
            return ImmutableArray<PortableExecutableReference>.Empty;
        }

        return projectPaths
                .Select(projectPath => MetadataReference.CreateFromFile(projectPath))
                .ToImmutableArray();
    }
}