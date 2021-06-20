using Microsoft.CodeAnalysis;
using PrettyPrompt.Consoles;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CSharpRepl.Services.Roslyn.MetadataResolvers
{
    class ProjectFileMetadataResolver : IChildMetadataReferenceResolver
    {
        private readonly IConsole console;

        public ProjectFileMetadataResolver(IConsole console)
        {
            this.console = console;
        }

        public ImmutableArray<PortableExecutableReference> ResolveReference(string reference, string baseFilePath, MetadataReferenceProperties properties, MetadataReferenceResolver rootResolver)
        {
            if (IsProjectReference(reference))
            {
                return LoadProjectReference(reference, baseFilePath, properties, rootResolver);
            }

            return ImmutableArray<PortableExecutableReference>.Empty;
        }

        private bool IsProjectReference(string reference)
        {
            var extension = Path.GetExtension(reference)?.ToLower();
            return extension == ".csproj" || extension == ".sln";
        }

        private ImmutableArray<PortableExecutableReference> LoadProjectReference(string reference, string baseFilePath, MetadataReferenceProperties properties, MetadataReferenceResolver rootResolver)
        {
            console.WriteLine("Building " + reference);
            var process = new Process
            {
                StartInfo =
                {
                    FileName = "dotnet.exe",
                    ArgumentList = { "build", reference },
                    RedirectStandardOutput = true
                }
            };

            var output = new List<string>();
            // Configure the process using the StartInfo properties.
            process.OutputDataReceived += (_, data) =>
            {
                if (data.Data is null) return;

                output.Add(data.Data);
                console.WriteLine(data.Data);
            };
            process.Start();
            process.BeginOutputReadLine();
            process.WaitForExit();

            if(process.ExitCode != 0)
            {
                console.WriteErrorLine("Project reference not added: received non-zero exit code from dotnet-build process");
                return ImmutableArray<PortableExecutableReference>.Empty;
            }

            var finalAssemblyOutput = output.LastOrDefault(line => line.Contains(" -> "));
            var assembly = finalAssemblyOutput
                ?.Split(" -> ", 2, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                 .LastOrDefault();

            if (finalAssemblyOutput is null)
            {
                console.WriteErrorLine("Project reference not added: could not determine built assembly");
                return ImmutableArray<PortableExecutableReference>.Empty;
            }

            console.WriteLine(string.Empty);
            console.WriteLine("Adding reference to " + assembly);

            return rootResolver.ResolveReference(assembly, baseFilePath, properties);
        }
    }
}
