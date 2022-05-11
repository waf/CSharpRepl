using CSharpRepl.Services.Roslyn.References;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Scripting;
using PrettyPrompt.Consoles;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using System.Text;
using System.Threading.Tasks;

namespace CSharpRepl.Services.Roslyn.MetadataResolvers;


internal sealed class SolutionFileMetadataResolver : IIndividualMetadataReferenceResolver
{
    private readonly AssemblyLoadContext loadContext;
    private readonly IConsole console;
    private readonly ImmutableArray<PortableExecutableReference> dummyPlaceholder;

    public SolutionFileMetadataResolver(IConsole console)
    {
        this.console = console;
        this.loadContext = new AssemblyLoadContext(nameof(CSharpRepl) + "LoadContext");
        this.dummyPlaceholder = new[] { MetadataReference.CreateFromFile(typeof(object).Assembly.Location) }.ToImmutableArray();
    }


    public ImmutableArray<PortableExecutableReference> ResolveReference(string reference, string? baseFilePath, MetadataReferenceProperties properties, MetadataReferenceResolver compositeResolver)
    {
        // This is a bit of a kludge. roslyn does not yet support adding multiple references from a single ResolveReference call, which
        // can happen with nuget packages (because they can have multiple DLLs and dependencies). https://github.com/dotnet/roslyn/issues/6900
        // We still want to use the "mostly standard" syntax of `#r "nuget:PackageName"` though, so make this a no-op and install the package
        // in the InstallNugetPackage method instead. Additional benefit is that we can use "real async" rather than needing to block here.
        if (IsSolutionReference(reference))
        {
            return dummyPlaceholder;
        }

        return ImmutableArray<PortableExecutableReference>.Empty;
    }

    public ImmutableArray<PortableExecutableReference> LoadSolutionReference(string reference)
    {
        console.WriteLine("Building " + reference);
        var solutionPath = reference.Split('\"', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Last();
        var (exitCode, output) = RunDotNetBuild(solutionPath);

        if (exitCode != 0)
        {
            console.WriteErrorLine("Project reference not added: received non-zero exit code from dotnet-build process");
            return ImmutableArray<PortableExecutableReference>.Empty;
        }

        var assemblies = GetAssemblies(output).ToArray();

        if (!assemblies.Any())
        {
            console.WriteErrorLine("Project reference not added: could not determine built assembly");
            return ImmutableArray<PortableExecutableReference>.Empty;
        }

        var allReferences = assemblies
                .Select(assembly => MetadataReference.CreateFromFile(Path.GetFullPath(assembly))) //GetFullPath will normalize separators
                .ToImmutableArray();

        return allReferences;
    }

    private (int exitCode, IReadOnlyList<string> linesOfOutput) RunDotNetBuild(string reference)
    {
        var output = new List<string>();

        var process = new Process
        {
            StartInfo =
                {
                    FileName = OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet",
                    ArgumentList = { "build", reference },
                    RedirectStandardOutput = true
                }
        };
        process.OutputDataReceived += (_, data) =>
        {
            if (data.Data is null) return;

            output.Add(data.Data);
            console.WriteLine(data.Data);
        };
        process.Start();
        process.BeginOutputReadLine();
        process.WaitForExit();

        return (process.ExitCode, output);
    }

    /// <summary>
    /// Parses the output of dotnet-build to determine every project that was build.
    /// </summary>
    /// <returns>
    /// Every project reference in the output.
    /// </returns>
    /// <remarks>
    /// Sample output that's being parsed below. We extract "C:\Projects\CSharpRepl\bin\Debug\net5.0\CSharpRepl.dll"
    ///
    /// Microsoft (R) Build Engine version 17.0.0-preview-21302-02+018bed83d for .NET
    /// Copyright (C) Microsoft Corporation. All rights reserved.
    /// 
    ///   Determining projects to restore...
    ///   All projects are up-to-date for restore.
    ///   You are using a preview version of .NET. See: https://aka.ms/dotnet-core-preview
    ///   CSharpRepl.Services -> C:\Projects\CSharpRepl.Services\bin\Debug\net5.0\CSharpRepl.Services.dll
    ///   CSharpRepl -> C:\Projects\CSharpRepl\bin\Debug\net5.0\CSharpRepl.dll
    /// 
    /// Build succeeded.
    ///     0 Warning(s)
    ///     0 Error(s)
    /// 
    /// Time Elapsed 00:00:02.15
    /// </remarks>
    private static IEnumerable<string> GetAssemblies(IReadOnlyList<string> output) =>
        output.Where(line => line.Contains(" -> "))
            .Select(line => line.Split(" -> ", 2, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .Select(splitted => splitted.Last());

    public static bool IsSolutionReference(string reference)
    {
        var extension = Path.GetExtension(reference.Split('\"', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Last())?.ToLower();
        return extension == ".sln";
    }
}
