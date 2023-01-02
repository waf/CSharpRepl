// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Immutable;
using System.Threading;
using System.Threading.Tasks;
using CSharpRepl.Services.Nuget;
using Microsoft.CodeAnalysis;

namespace CSharpRepl.Services.Roslyn.MetadataResolvers;

/// <summary>
/// Resolves nuget references, e.g. #r "nuget: Newtonsoft.Json" or #r "nuget: Newtonsoft.Json, 13.0.1"
/// </summary>
internal sealed class NugetPackageMetadataResolver : AlternativeReferenceResolver
{
    private const string NugetPrefix = "nuget:";
    private const string NugetPrefixWithHashR = "#r \"" + NugetPrefix;
    private readonly NugetPackageInstaller nugetInstaller;

    public NugetPackageMetadataResolver(IConsoleEx console, Configuration configuration)
    {
        this.nugetInstaller = new NugetPackageInstaller(console, configuration);
    }

    public override bool CanResolve(string reference) =>
        reference.StartsWith(NugetPrefix, StringComparison.OrdinalIgnoreCase) ||
        reference.StartsWith(NugetPrefixWithHashR, StringComparison.OrdinalIgnoreCase); // roslyn trims the "#r" prefix when passing to the resolver, but it has the prefix when called from our ScriptRunner

    public override Task<ImmutableArray<PortableExecutableReference>> ResolveAsync(string reference, CancellationToken cancellationToken)
    {
        // we can be a bit loose in our parsing here, because we were more strict in IsNugetReference.
        // the 0th element will be the "nuget" keyword, which we ignore.
        var packageParts = reference.Split(
            new[] { "#r", "\"", ":", " ", ",", "/", "\\" },
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries
        );

        return packageParts.Length switch
        {
            2 => nugetInstaller.InstallAsync(packageId: packageParts[1], cancellationToken: cancellationToken),
            3 => nugetInstaller.InstallAsync(packageId: packageParts[1], version: packageParts[2].TrimStart('v'), cancellationToken),
            _ => throw new InvalidOperationException(@"Malformed nuget reference. Expected #r ""nuget: PackageName"" or #r ""nuget: PackageName, version""")
        };
    }
}