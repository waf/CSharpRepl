// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.IO;
using System.Linq;
using CSharpRepl.Services;
using CSharpRepl.Services.Roslyn;
using CSharpRepl.Services.Roslyn.References;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace CSharpRepl.Tests;

[Collection(nameof(RoslynServices))]
public class AssemblyReferenceServiceTests
{
    private static readonly CSharpParseOptions parseOptions =
        CSharpParseOptions.Default.WithKind(SourceCodeKind.Script).WithLanguageVersion(LanguageVersion.Latest);

    [Fact]
    public void GetSharedFrameworkConfiguration_CalledRepeatedly_DoesNotRecreateReferences()
    {
        var service = new AssemblyReferenceService(new Configuration(), parseOptions, new TestTraceLogger());

        var first = service.GetSharedFrameworkConfiguration(SharedFramework.NetCoreApp, Environment.Version);
        var second = service.GetSharedFrameworkConfiguration(SharedFramework.NetCoreApp, Environment.Version);

        Assert.Same(first.Single(), second.Single());

        // the constructor's initial load should also share the same references, rather than having loaded its own copy.
        Assert.Contains(service.LoadedReferenceAssemblies, r => ReferenceEquals(r, first.Single().ReferenceAssemblies.First()));
    }

    [Fact]
    public void EnsureReferenceAssembly_GivenImplementationAssembly_ReusesLoadedReferenceAssembly()
    {
        var service = new AssemblyReferenceService(new Configuration(), parseOptions, new TestTraceLogger());

        var implementationPath = service.ImplementationAssemblyPaths
            .Select(path => Path.Combine(path, "System.Linq.dll"))
            .First(File.Exists);

        // the implementation assembly should be mapped to the reference assembly instance that was
        // already loaded at startup, not to a freshly loaded second copy of it.
        var mappedReference = service.EnsureReferenceAssembly(MetadataReference.CreateFromFile(implementationPath));

        Assert.NotNull(mappedReference);
        Assert.Contains(service.LoadedReferenceAssemblies, r => ReferenceEquals(r, mappedReference));
    }
}
