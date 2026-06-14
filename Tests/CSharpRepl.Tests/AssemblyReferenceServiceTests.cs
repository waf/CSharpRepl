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

    [Fact] // https://github.com/waf/CSharpRepl/issues/355
    public void RemoveDuplicateReferences_ConflictingVersions_KeepsOnlyTheHighest()
    {
        var service = new AssemblyReferenceService(new Configuration(), parseOptions, new TestTraceLogger());

        var older = EmitAssembly("CSharpRepl_ConflictLib", new Version(1, 0, 0, 0));
        var newer = EmitAssembly("CSharpRepl_ConflictLib", new Version(2, 0, 0, 0));
        var unrelated = EmitAssembly("CSharpRepl_OtherLib", new Version(1, 0, 0, 0));

        var unified = service.RemoveDuplicateReferences(
        [
            MetadataReference.CreateFromFile(older),
            MetadataReference.CreateFromFile(newer),
            MetadataReference.CreateFromFile(unrelated),
        ]);

        // The two conflicting versions collapse to the single highest one (so the same type can't appear under two
        // assembly identities), while the unrelated assembly is left untouched.
        var conflictLib = unified.OfType<PortableExecutableReference>().Where(r => r.FilePath?.Contains("ConflictLib") == true).ToList();
        Assert.Equal(newer, Assert.Single(conflictLib).FilePath);
        Assert.Contains(unified, r => (r as PortableExecutableReference)?.FilePath == unrelated);
    }

    [Fact] // https://github.com/waf/CSharpRepl/issues/355
    public void RemoveDuplicateReferences_SameVersionFromMultiplePaths_KeepsAll()
    {
        // The same assembly identity resolved from multiple paths (e.g. a #r'd project that is also already loaded)
        // is not a real conflict, so all copies are kept and Roslyn deduplicates them itself.
        var service = new AssemblyReferenceService(new Configuration(), parseOptions, new TestTraceLogger());

        var copy1 = EmitAssembly("CSharpRepl_SameVersionLib", new Version(1, 2, 3, 4));
        var copy2 = EmitAssembly("CSharpRepl_SameVersionLib", new Version(1, 2, 3, 4));

        var unified = service.RemoveDuplicateReferences(
        [
            MetadataReference.CreateFromFile(copy1),
            MetadataReference.CreateFromFile(copy2),
        ]);

        Assert.Equal(2, unified.OfType<PortableExecutableReference>().Count(r => r.FilePath?.Contains("SameVersionLib") == true));
    }

    private static string EmitAssembly(string name, Version version)
    {
        var path = Path.Combine(Path.GetTempPath(), $"{name}_{version.ToString().Replace('.', '_')}_{Guid.NewGuid():N}.dll");
        var compilation = CSharpCompilation.Create(
            name,
            [CSharpSyntaxTree.ParseText($"[assembly: System.Reflection.AssemblyVersion(\"{version}\")] public class Marker {{ }}")],
            [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)],
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var emit = compilation.Emit(path);
        Assert.True(emit.Success, string.Join(Environment.NewLine, emit.Diagnostics));
        return path;
    }
}
