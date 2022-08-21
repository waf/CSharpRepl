// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Immutable;
using System.Linq;
using CSharpRepl.Services.Dotnet;
using Microsoft.CodeAnalysis;
using PrettyPrompt.Consoles;

namespace CSharpRepl.Services.Roslyn.MetadataResolvers;

/// <summary>
/// Allows referencing a csproj, e.g. #r "path/to/foo.csproj"
/// Simply runs "dotnet build" on the csproj and then resolves the final assembly.
/// </summary>
internal sealed class ProjectFileMetadataResolver : IIndividualMetadataReferenceResolver
{
    private readonly DotnetBuilder builder;
    private readonly IConsole console;

    public ProjectFileMetadataResolver(DotnetBuilder builder, IConsole console)
    {
        this.builder = builder;
        this.console = console;
    }

    public ImmutableArray<PortableExecutableReference> ResolveReference(string reference, string? baseFilePath, MetadataReferenceProperties properties, MetadataReferenceResolver compositeResolver)
    {
        if (IsProjectReference(reference))
            return LoadProjectReference(reference, baseFilePath, properties, compositeResolver);

        return ImmutableArray<PortableExecutableReference>.Empty;
    }

    private static bool IsProjectReference(string reference)
        => reference.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase);

    private ImmutableArray<PortableExecutableReference> LoadProjectReference(string reference, string? baseFilePath, MetadataReferenceProperties properties, MetadataReferenceResolver compositeResolver)
    {
        var (exitCode, output) = builder.Build(reference);

        if (exitCode != 0)
        {
            console.WriteErrorLine("Project reference not added: received non-zero exit code from dotnet-build process");
            return ImmutableArray<PortableExecutableReference>.Empty;
        }

        var assemblyGraph = builder.ParseBuildGraph(output);
        var assembly = assemblyGraph.LastOrDefault().Value;

        if (assembly is null)
        {
            console.WriteErrorLine("Project reference not added: could not determine built assembly");
            return ImmutableArray<PortableExecutableReference>.Empty;
        }

        console.WriteLine(string.Empty);
        console.WriteLine("Adding reference to " + assembly);

        return compositeResolver.ResolveReference(assembly, baseFilePath, properties);
    }
}