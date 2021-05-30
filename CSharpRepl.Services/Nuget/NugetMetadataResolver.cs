#region License Header
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.
#endregion
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Scripting;
using PrettyPrompt.Consoles;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using System.Threading;
using System.Threading.Tasks;

namespace Sharply.Services.Nuget
{
    public class NugetMetadataResolver : MetadataReferenceResolver
    {
        private const string NugetPrefix = "nuget:";
        private readonly IConsole console;
        private readonly ScriptMetadataResolver defaultResolver;
        private readonly HashSet<string> assemblyPaths;
        private readonly AssemblyLoadContext loadContext;
        private readonly NugetPackageInstaller nugetInstaller;
        private readonly ImmutableArray<PortableExecutableReference> dummyPlaceholder;

        public NugetMetadataResolver(IConsole console, IReadOnlyCollection<string> implementationAssemblyPaths)
        {
            this.console = console;
            this.defaultResolver = ScriptMetadataResolver.Default;
            this.assemblyPaths = new HashSet<string>(implementationAssemblyPaths);
            this.loadContext = new AssemblyLoadContext(nameof(Sharply) + "Repl");
            this.nugetInstaller = new NugetPackageInstaller(console);
            this.dummyPlaceholder = new[] { MetadataReference.CreateFromFile(typeof(object).Assembly.Location) }.ToImmutableArray();

            AppDomain.CurrentDomain.AssemblyResolve += new ResolveEventHandler(ChooseBestMatch);
        }

        public override ImmutableArray<PortableExecutableReference> ResolveReference(string reference, string baseFilePath, MetadataReferenceProperties properties)
        {
            reference = reference.Trim();

            // This is a bit of a kludge. roslyn does not yet support adding multiple references from a single ResolveReference call, which
            // can happen with nuget packages (because they can have multiple DLLs and dependencies). https://github.com/dotnet/roslyn/issues/6900
            // We still want to use the "mostly standard" syntax of `#r "nuget:PackageName"` though, so make this a no-op and install the package
            // in the InstallNugetPackage method instead. Additional benefit is that we can use "real async" rather than needing to block here.
            if (IsNugetReference(reference))
            {
                return dummyPlaceholder;
            }

            assemblyPaths.Add(Path.GetDirectoryName(reference));
            var references = defaultResolver.WithSearchPaths(assemblyPaths).ResolveReference(reference, baseFilePath, properties);

            return references;
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

        /// <summary>
        /// If we're missing an assembly (by exact match), try to find an assembly with the same name but different version.
        /// </summary>
        private Assembly ChooseBestMatch(object sender, ResolveEventArgs args)
        {
            var assemblyName = new AssemblyName(args.Name);

            var located = this.assemblyPaths
                .SelectMany(path => Directory.GetFiles(path, "*.dll"))
                .Where(file => Path.GetFileNameWithoutExtension(file) == assemblyName.Name)
                .Select(loadContext.LoadFromAssemblyPath)
                .FirstOrDefault();
            
            if (located is not null && new AssemblyName(located.FullName).Version != assemblyName.Version)
            {
                console.WriteLine($@"Warning: Missing assembly: {args.Name}");
                console.WriteLine($@"            Using instead: {located.FullName}");
                console.WriteLine($@"             Requested by: {args.RequestingAssembly.FullName}");
            }

            return located;
        }

        public override bool ResolveMissingAssemblies =>
            defaultResolver.ResolveMissingAssemblies;

        public override PortableExecutableReference ResolveMissingAssembly(MetadataReference definition, AssemblyIdentity referenceIdentity) =>
            defaultResolver.ResolveMissingAssembly(definition, referenceIdentity);

        public override bool Equals(object other) =>
            defaultResolver.Equals(other);

        public override int GetHashCode() =>
            defaultResolver.GetHashCode();
    }
}
