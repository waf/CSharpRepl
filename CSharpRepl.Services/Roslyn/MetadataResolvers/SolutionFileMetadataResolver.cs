using CSharpRepl.Services.Dotnet;
using Microsoft.CodeAnalysis;
using PrettyPrompt.Consoles;
using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;

namespace CSharpRepl.Services.Roslyn.MetadataResolvers;


internal sealed class SolutionFileMetadataResolver : IIndividualMetadataReferenceResolver
{
    private readonly ImmutableArray<PortableExecutableReference> dummyPlaceholder;

    private readonly IDotnetBuilder builder;
    private readonly IConsole console;

    public SolutionFileMetadataResolver(IDotnetBuilder builder, IConsole console)
    {
        this.builder = builder;
        this.console = console;
        this.dummyPlaceholder = new[] { MetadataReference.CreateFromFile(typeof(object).Assembly.Location) }.ToImmutableArray();
    }

    public ImmutableArray<PortableExecutableReference> ResolveReference(string reference, string? baseFilePath, MetadataReferenceProperties properties, MetadataReferenceResolver compositeResolver)
    {
        if (IsSolutionReference(reference))
            return dummyPlaceholder;

        return ImmutableArray<PortableExecutableReference>.Empty;
    }

    public ImmutableArray<PortableExecutableReference> LoadSolutionReference(string reference)
    {
        var solutionPath = Path.GetFullPath(reference
            .Split('\"', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Last());

        var (exitCode, output) = builder.Build(solutionPath);

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

    public static bool IsSolutionReference(string reference)
    {
        var extension = Path.GetExtension(reference.Split('\"', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Last())?.ToLower();
        return extension == ".sln";
    }
}
