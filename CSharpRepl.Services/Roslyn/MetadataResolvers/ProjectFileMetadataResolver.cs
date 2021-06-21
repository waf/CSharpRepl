// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Microsoft.CodeAnalysis;
using PrettyPrompt.Consoles;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace CSharpRepl.Services.Roslyn.MetadataResolvers
{
    /// <summary>
    /// Allows referencing a csproj or sln, e.g. #r "path/to/foo.csproj" or #r "path/to/foo.sln"
    /// Simply runs "dotnet build" on the csproj/sln and then resolves the final assembly.
    /// </summary>
    internal class ProjectFileMetadataResolver : IIndividualMetadataReferenceResolver
    {
        private readonly IConsole console;

        public ProjectFileMetadataResolver(IConsole console)
        {
            this.console = console;
        }

        public ImmutableArray<PortableExecutableReference> ResolveReference(string reference, string baseFilePath, MetadataReferenceProperties properties, MetadataReferenceResolver compositeResolver)
        {
            if (IsProjectReference(reference))
            {
                return LoadProjectReference(reference, baseFilePath, properties, compositeResolver);
            }

            return ImmutableArray<PortableExecutableReference>.Empty;
        }

        private static bool IsProjectReference(string reference)
        {
            var extension = Path.GetExtension(reference)?.ToLower();
            return extension == ".csproj" || extension == ".sln";
        }

        private ImmutableArray<PortableExecutableReference> LoadProjectReference(string reference, string baseFilePath, MetadataReferenceProperties properties, MetadataReferenceResolver compositeResolver)
        {
            console.WriteLine("Building " + reference);
            var (exitCode, output) = RunDotNetBuild(reference);

            if (exitCode != 0)
            {
                console.WriteErrorLine("Project reference not added: received non-zero exit code from dotnet-build process");
                return ImmutableArray<PortableExecutableReference>.Empty;
            }

            string assembly = ParseBuildOutput(output);

            if (assembly is null)
            {
                console.WriteErrorLine("Project reference not added: could not determine built assembly");
                return ImmutableArray<PortableExecutableReference>.Empty;
            }

            console.WriteLine(string.Empty);
            console.WriteLine("Adding reference to " + assembly);

            return compositeResolver.ResolveReference(assembly, baseFilePath, properties);
        }

        private (int exitCode, IReadOnlyList<string> linesOfOutput) RunDotNetBuild(string reference)
        {
            var output = new List<string>();

            var process = new Process
            {
                StartInfo =
                {
                    FileName = "dotnet.exe",
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
        /// Parse the output of dotnet-build to determine the assembly that was build.
        /// </summary>
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
        private static string ParseBuildOutput(IReadOnlyList<string> output) =>
            output
                .LastOrDefault(line => line.Contains(" -> "))
                ?.Split(" -> ", 2, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .LastOrDefault();
    }
}
