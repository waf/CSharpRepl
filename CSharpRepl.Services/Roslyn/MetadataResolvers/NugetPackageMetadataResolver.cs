// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Immutable;
using System.Threading;
using System.Threading.Tasks;
using CSharpRepl.Services.Nuget;
using CSharpRepl.Services.Roslyn.References;
using Microsoft.Build.Locator;
using Microsoft.CodeAnalysis;

namespace CSharpRepl.Services.Roslyn.MetadataResolvers;

/// <summary>
/// Resolves nuget references, e.g. #r "nuget: Newtonsoft.Json" or #r "nuget: Newtonsoft.Json, 13.0.1"
/// </summary>
internal sealed class NugetPackageMetadataResolver(IConsoleService console, Configuration configuration, AssemblyReferenceService referenceAssemblyService, ReplAssemblyLoader? assemblyLoader = null) : AlternativeReferenceResolver
{
    private const string NugetPrefix = "nuget:";
    private const string NugetPrefixWithHashR = "#r \"" + NugetPrefix;
    // Lazy, because constructing it loads NuGet assemblies from the .NET SDK, which aren't available if MSBuildLocator
    // found no compatible SDK. Creating it eagerly crashed the REPL at startup. https://github.com/waf/CSharpRepl/issues/526
    private readonly Lazy<NugetPackageInstaller> nugetInstaller = new(() => new(console, configuration, referenceAssemblyService, assemblyLoader));

    public override bool CanResolve(string reference) =>
        reference.StartsWith(NugetPrefix, StringComparison.OrdinalIgnoreCase) ||
        reference.StartsWith(NugetPrefixWithHashR, StringComparison.OrdinalIgnoreCase); // roslyn trims the "#r" prefix when passing to the resolver, but it has the prefix when called from our ScriptRunner

    public override Task<ImmutableArray<PortableExecutableReference>> ResolveAsync(string reference, CancellationToken cancellationToken)
    {
        // we can be a bit loose in our parsing here, because we were more strict in IsNugetReference.
        // the 0th element will be the "nuget" keyword, which we ignore.
        var packageParts = reference.Split(
            ["#r", "\"", ":", " ", ",", "/", "\\"],
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries
        );

        // MSBuildLocator ignores SDKs newer than the runtime we're running on, so e.g. with only a preview SDK of the next
        // .NET version installed, nothing is registered and the NuGet assemblies can't load. https://github.com/waf/CSharpRepl/issues/526
        // Possible mitigation for users, UNTESTED: DOTNET_ROLL_FORWARD_TO_PRERELEASE=1 should let csharprepl roll forward
        // onto that preview SDK's runtime, which would make the SDK usable.
        if (!MSBuildLocator.IsRegistered)
        {
            throw new InvalidOperationException($"NuGet package references require a .NET SDK compatible with the .NET {Environment.Version} runtime, but none was found. Install the .NET {Environment.Version.Major} SDK from https://dotnet.microsoft.com/download");
        }

        return packageParts.Length switch
        {
            2 => nugetInstaller.Value.InstallAsync(packageId: packageParts[1], cancellationToken: cancellationToken),
            3 => nugetInstaller.Value.InstallAsync(packageId: packageParts[1], version: packageParts[2].TrimStart('v'), cancellationToken),
            _ => throw new InvalidOperationException(@"Malformed nuget reference. Expected #r ""nuget: PackageName"" or #r ""nuget: PackageName, version""")
        };
    }
}