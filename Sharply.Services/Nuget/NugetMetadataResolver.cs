using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Scripting;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Sharply.Services.Nuget
{
    public class NugetMetadataResolver : MetadataReferenceResolver
    {
        private const string NugetPrefix = "nuget:";
        private readonly ScriptMetadataResolver defaultResolver;
        private readonly NugetPackageInstaller nugetInstaller;
        private readonly ImmutableArray<PortableExecutableReference> dummyPlaceholder;

        public NugetMetadataResolver(IReadOnlyCollection<string> implementationAssemblyPaths)
        {
            this.defaultResolver = ScriptMetadataResolver.Default
                .WithSearchPaths(implementationAssemblyPaths.ToArray());
            this.nugetInstaller = new NugetPackageInstaller();
            this.dummyPlaceholder = new[] { MetadataReference.CreateFromFile(typeof(object).Assembly.Location) }.ToImmutableArray();
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

            var references = defaultResolver.ResolveReference(reference, baseFilePath, properties);

            return references;
        }

        public bool IsNugetReference(string reference) =>
            reference.ToLowerInvariant().StartsWith(NugetPrefix) // roslyn trims the "#r" prefix when passing to the resolver
            || reference.ToLowerInvariant().StartsWith($"#r \"{NugetPrefix}");

        public Task<ImmutableArray<PortableExecutableReference>> InstallNugetPackage(string reference, CancellationToken cancellationToken)
        {
            // we can be a bit loose in our parsing here, because we were more strict in IsNugetReference.
            var packageParts = reference.Split(
                new[] {"#r", "\"", "nuget", ":",  " ", ",", "/", "\\" },
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries
            );

            if (packageParts.Length == 1)
            {
                return nugetInstaller.Install(packageParts[0], cancellationToken: cancellationToken);
            }
            if (packageParts.Length == 2)
            {
                return nugetInstaller.Install(packageParts[0], packageParts[1].TrimStart('v'), cancellationToken);
            }

            throw new InvalidOperationException(@"Malformed nuget reference. Expected #r ""nuget: PackageName"" or #r ""nuget: PackageName, version""");
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
