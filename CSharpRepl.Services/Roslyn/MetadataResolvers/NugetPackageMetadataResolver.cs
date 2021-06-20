// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using CSharpRepl.Services.Roslyn;
using CSharpRepl.Services.Roslyn.MetadataResolvers;
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
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace CSharpRepl.Services.Nuget
{
    internal class NugetPackageMetadataResolver : IChildMetadataReferenceResolver
    {
        private const string NugetPrefix = "nuget:";
        private readonly NugetPackageInstaller nugetInstaller;
        private readonly ImmutableArray<PortableExecutableReference> dummyPlaceholder;

        public NugetPackageMetadataResolver(IConsole console, ReferenceAssemblyService referenceAssemblyService)
        {
            this.nugetInstaller = new NugetPackageInstaller(console);
            this.dummyPlaceholder = new[] { MetadataReference.CreateFromFile(typeof(object).Assembly.Location) }.ToImmutableArray();
        }

        public ImmutableArray<PortableExecutableReference> ResolveReference(string reference, string baseFilePath, MetadataReferenceProperties properties, MetadataReferenceResolver rootResolver)
        {
            // This is a bit of a kludge. roslyn does not yet support adding multiple references from a single ResolveReference call, which
            // can happen with nuget packages (because they can have multiple DLLs and dependencies). https://github.com/dotnet/roslyn/issues/6900
            // We still want to use the "mostly standard" syntax of `#r "nuget:PackageName"` though, so make this a no-op and install the package
            // in the InstallNugetPackage method instead. Additional benefit is that we can use "real async" rather than needing to block here.
            if (IsNugetReference(reference))
            {
                return dummyPlaceholder;
            }

            return ImmutableArray<PortableExecutableReference>.Empty;
        }

        public bool IsNugetReference(string reference) =>
            reference.ToLowerInvariant() is string lowercased
            && (lowercased.StartsWith(NugetPrefix) || lowercased.StartsWith($"#r \"{NugetPrefix}")); // roslyn trims the "#r" prefix when passing to the resolver, but it has the prefix when called from our ScriptRunner

        public Task<ImmutableArray<PortableExecutableReference>> InstallNugetPackageAsync(string reference, CancellationToken cancellationToken)
        {
            // we can be a bit loose in our parsing here, because we were more strict in IsNugetReference.
            var packageParts = reference.Split(
                new[] {"#r", "\"", "nuget", ":",  " ", ",", "/", "\\" },
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries
            );

            return packageParts.Length switch
            {
                1 => nugetInstaller.InstallAsync(packageId: packageParts[0], cancellationToken: cancellationToken),
                2 => nugetInstaller.InstallAsync(packageId: packageParts[0], version: packageParts[1].TrimStart('v'), cancellationToken: cancellationToken),
                _ => throw new InvalidOperationException(@"Malformed nuget reference. Expected #r ""nuget: PackageName"" or #r ""nuget: PackageName, version""")
            };
        }
    }
}
