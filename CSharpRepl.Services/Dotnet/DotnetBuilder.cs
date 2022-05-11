using PrettyPrompt.Consoles;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace CSharpRepl.Services.Dotnet;

internal interface IDotnetBuilder
{
    ImmutableDictionary<string, string> ParseBuildGraph(ImmutableArray<string> buildOutput);
    (int exitCode, ImmutableArray<string> outputLines) Build(string path);
    Task<(int exitCode, ImmutableArray<string> outputLines)> BuildAsync(string path, CancellationToken cancellationToken);
}

internal class DotnetBuilder : IDotnetBuilder
{
    private readonly IConsole console;

    public DotnetBuilder(IConsole console)
    {
        this.console = console;
    }

    public (int exitCode, ImmutableArray<string> outputLines) Build(string path)
    {
        var output = new List<string>();

        var process = new Process
        {
            StartInfo =
            {
                FileName = OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet",
                ArgumentList = { "build", path },
                RedirectStandardOutput = true
            }
        };
        process.OutputDataReceived += (_, data) =>
        {
            if (data.Data is null) return;

            output.Add(data.Data);
            console.WriteLine(data.Data);
        };
        console.WriteLine("Building " + path);
        process.Start();
        process.BeginOutputReadLine();
        process.WaitForExit();

        return (process.ExitCode, output.ToImmutableArray());
    }

    public async Task<(int exitCode, ImmutableArray<string> outputLines)> BuildAsync(string path, CancellationToken cancellationToken)
    {
        var output = new List<string>();

        var process = new Process
        {
            StartInfo =
            {
                FileName = OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet",
                ArgumentList = { "build", path },
                RedirectStandardOutput = true
            }
        };
        process.OutputDataReceived += (_, data) =>
        {
            if (data.Data is null) return;

            output.Add(data.Data);
            console.WriteLine(data.Data);
        };
        console.WriteLine("Building " + path);
        process.Start();
        process.BeginOutputReadLine();
        await process.WaitForExitAsync(cancellationToken);

        return (process.ExitCode, output.ToImmutableArray());
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
    public ImmutableDictionary<string, string> ParseBuildGraph(ImmutableArray<string> buildOutput)
    {
        return buildOutput
            .Where(line => line.Contains(" -> "))
            .Select(line => line.Split(" -> ", 2, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .ToImmutableDictionary(x => x.First(), x => x.Last());
    }
}
