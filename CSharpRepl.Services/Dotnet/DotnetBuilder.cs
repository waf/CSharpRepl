// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace CSharpRepl.Services.Dotnet;

internal class DotnetBuilder
{
    private readonly IConsoleEx console;

    public DotnetBuilder(IConsoleEx console)
    {
        this.console = console;
    }

    public (int exitCode, ImmutableArray<string> outputLines) Build(string path)
    {
        using var process = StartBuild(path, out var output);
        process.WaitForExit();
        return (process.ExitCode, output.ToImmutableArray());
    }

    public async Task<(int exitCode, ImmutableArray<string> outputLines)> BuildAsync(string path, CancellationToken cancellationToken)
    {
        using var process = StartBuild(path, out var output);
        await process.WaitForExitAsync(cancellationToken);
        return (process.ExitCode, output.ToImmutableArray());
    }

    private Process StartBuild(string path, out List<string> output)
    {
        output = new List<string>();
        var process = new Process
        {
            StartInfo =
          {
              FileName = OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet",
              ArgumentList = { "build", path },
              RedirectStandardOutput = true
          }
        };

        var outputForClosure = output;
        process.OutputDataReceived += (_, data) =>
        {
            if (data.Data is null) return;

            outputForClosure.Add(data.Data);
            console.WriteLine(data.Data);
        };

        console.WriteLine("Building " + path);
        process.Start();
        process.BeginOutputReadLine();
        return process;
    }
}
