// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CSharpRepl.Services;
using CSharpRepl.Services.Dotnet;
using CSharpRepl.Services.Roslyn;
using CSharpRepl.Services.Roslyn.Scripting;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace CSharpRepl.Tests;

[Collection(nameof(RoslynServices))]
public class EvaluationTests : IAsyncLifetime
{
    private readonly RoslynServices services;
    private readonly StringBuilder stdout;
    private readonly IConsoleService console;

    public EvaluationTests()
    {
        (console, stdout) = FakeConsole.CreateStubbedOutput();
        this.services = new RoslynServices(console, new Configuration(), new TestTraceLogger());
    }

    public ValueTask InitializeAsync() => new(services.WarmUpAsync([]));
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task Evaluate_LiteralInteger_ReturnsInteger()
    {
        var result = await services.EvaluateAsync("5", cancellationToken: TestContext.Current.CancellationToken);

        var success = Assert.IsType<EvaluationResult.Success>(result);
        Assert.Equal("5", success.Input);

        var returnValue = Assert.IsType<int>(success.ReturnValue.Value);
        Assert.Equal(5, returnValue);
    }

    [Fact]
    public async Task Evaluate_FailingDebugAssert_DoesNotCrashAndReturnsError()
    {
        // Without DebugAssertHandler, a failed Debug.Assert FailFasts and crashes the whole process.
        // Debug.Assert is [Conditional("DEBUG")], so the submission must define DEBUG for it to emit.
        // See https://github.com/waf/CSharpRepl/issues/374.
        var result = await services.EvaluateAsync(
            "#define DEBUG\nSystem.Diagnostics.Debug.Assert(false, \"boom\");",
            cancellationToken: TestContext.Current.CancellationToken);

        var error = Assert.IsType<EvaluationResult.Error>(result);
        Assert.Contains("boom", error.Exception.Message);
    }

    [Fact]
    public async Task Evaluate_Variable_ReturnsValue()
    {
        var variableAssignment = await services.EvaluateAsync(@"var x = ""Hello World"";", cancellationToken: TestContext.Current.CancellationToken);
        var variableUsage = await services.EvaluateAsync(@"x.Replace(""World"", ""Mundo"")", cancellationToken: TestContext.Current.CancellationToken);

        var assignment = Assert.IsType<EvaluationResult.Success>(variableAssignment);
        var usage = Assert.IsType<EvaluationResult.Success>(variableUsage);
        Assert.Null(assignment.ReturnValue.Value);
        Assert.Equal("Hello Mundo", usage.ReturnValue);
    }

    [Fact]
    public async Task Evaluate_NugetPackage_InstallsPackage()
    {
        var installation = await services.EvaluateAsync(@"#r ""nuget:Newtonsoft.Json""", cancellationToken: TestContext.Current.CancellationToken);
        var usage = await services.EvaluateAsync(@"Newtonsoft.Json.JsonConvert.SerializeObject(new { Foo = ""bar"" })", cancellationToken: TestContext.Current.CancellationToken);

        var installationResult = Assert.IsType<EvaluationResult.Success>(installation);
        var usageResult = Assert.IsType<EvaluationResult.Success>(usage);

        Assert.Null(installationResult.ReturnValue.Value);
        Assert.Contains(installationResult.References, r => r.Display.EndsWith("Newtonsoft.Json.dll"));
        Assert.Contains("Adding references for 'Newtonsoft.Json", stdout.ToString().RemoveFormatting());
        Assert.Equal(@"{""Foo"":""bar""}", usageResult.ReturnValue);
    }

    [Fact]
    public async Task Evaluate_NugetPackageVersioned_InstallsPackageVersion()
    {
        var installation = await services.EvaluateAsync(@"#r ""nuget:Microsoft.CodeAnalysis.CSharp, 3.11.0""", cancellationToken: TestContext.Current.CancellationToken);
        var usage = await services.EvaluateAsync(@"Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree.ParseText(""5"")", cancellationToken: TestContext.Current.CancellationToken);

        var installationResult = Assert.IsType<EvaluationResult.Success>(installation);
        var usageResult = Assert.IsType<EvaluationResult.Success>(usage);

        Assert.Null(installationResult.ReturnValue.Value);
        Assert.NotNull(usageResult.ReturnValue.Value);
        Assert.Contains("Adding references for 'Microsoft.CodeAnalysis.CSharp.3.11.0'", stdout.ToString().RemoveFormatting());
    }

    [Fact]
    public async Task Evaluate_RelativeAssemblyReference_CanReferenceAssembly()
    {
        var referenceResult = await services.EvaluateAsync(@"#r ""./Data/DemoLibrary.dll""", cancellationToken: TestContext.Current.CancellationToken);
        var importResult = await services.EvaluateAsync("using DemoLibrary;", cancellationToken: TestContext.Current.CancellationToken);
        var multiplyResult = await services.EvaluateAsync("DemoClass.Multiply(5, 6)", cancellationToken: TestContext.Current.CancellationToken);

        Assert.IsType<EvaluationResult.Success>(referenceResult);
        Assert.IsType<EvaluationResult.Success>(importResult);
        var successfulResult = Assert.IsType<EvaluationResult.Success>(multiplyResult);
        Assert.Equal(30, successfulResult.ReturnValue);
    }

    [Fact]
    public async Task Evaluate_AbsoluteAssemblyReference_CanReferenceAssembly()
    {
        var absolutePath = Path.GetFullPath("./Data/DemoLibrary.dll");
        var referenceResult = await services.EvaluateAsync(@$"#r ""{absolutePath}""", cancellationToken: TestContext.Current.CancellationToken);
        var importResult = await services.EvaluateAsync("using DemoLibrary;", cancellationToken: TestContext.Current.CancellationToken);
        var multiplyResult = await services.EvaluateAsync("DemoClass.Multiply(7, 6)", cancellationToken: TestContext.Current.CancellationToken);

        Assert.IsType<EvaluationResult.Success>(referenceResult);
        Assert.IsType<EvaluationResult.Success>(importResult);
        var successfulResult = Assert.IsType<EvaluationResult.Success>(multiplyResult);
        Assert.Equal(42, successfulResult.ReturnValue);
    }

    [Fact]
    public async Task Evaluate_AssemblyReferenceInSearchPath_CanReferenceAssembly()
    {
        var referenceResult = await services.EvaluateAsync(@"#r ""System.Linq.dll""", cancellationToken: TestContext.Current.CancellationToken);

        Assert.IsType<EvaluationResult.Success>(referenceResult);
    }

    [Fact]
    public async Task Evaluate_AssemblyReferenceWithSharedFramework_ReferencesSharedFramework()
    {
        var referenceResult = await services.EvaluateAsync(@"#r ""./Data/WebApplication1.dll""", cancellationToken: TestContext.Current.CancellationToken);
        var sharedFrameworkResult = await services.EvaluateAsync(@"using Microsoft.AspNetCore.Hosting;", cancellationToken: TestContext.Current.CancellationToken);
        var applicationResult = await services.EvaluateAsync(@"using WebApplication1;", cancellationToken: TestContext.Current.CancellationToken);

        Assert.IsType<EvaluationResult.Success>(referenceResult);
        Assert.IsType<EvaluationResult.Success>(sharedFrameworkResult);
        Assert.IsType<EvaluationResult.Success>(applicationResult);

        var completions = await services.CompleteAsync(@"using WebApplicat", 17, TestContext.Current.CancellationToken);
        Assert.Contains("WebApplication1", completions.Select(c => c.Item.DisplayText).First(text => text.StartsWith("WebApplicat")));
    }

    [Fact]
    public async Task Evaluate_RunSharedFrameworkCode_DoesNotThrowAssemblyLoadException()
    {
        // Regression test for https://github.com/waf/CSharpRepl/issues/414. Referencing the ASP.NET Core shared framework and 
        // then running code that transitively loaded Microsoft.Extensions.Logging / Microsoft.Extensions.DependencyInjection threw
        // the exception "Manifest definition does not match the assembly reference", because we bundled those assemblies
        // at a lower version than the shared framework (they are dependencies of Microsoft.CodeAnalysis.Workspaces.MSBuild)
        await services.EvaluateAsync(@"#r ""./Data/WebApplication1.dll""", cancellationToken: TestContext.Current.CancellationToken);
        var result = await services.EvaluateAsync(
            @"Microsoft.AspNetCore.Builder.WebApplication.CreateBuilder().Build().GetType().Name",
            cancellationToken: TestContext.Current.CancellationToken);

        var success = Assert.IsType<EvaluationResult.Success>(result);
        Assert.Equal("WebApplication", success.ReturnValue.Value);
    }

    [Fact]
    public async Task Evaluate_ProjectReference_ReferencesProject()
    {
        var referenceResult = await services.EvaluateAsync(@"#r ""./../../../../../CSharpRepl.Services/CSharpRepl.Services.csproj""", cancellationToken: TestContext.Current.CancellationToken);
        var importResult = await services.EvaluateAsync(@"using CSharpRepl.Services;", cancellationToken: TestContext.Current.CancellationToken);

        Assert.IsType<EvaluationResult.Success>(referenceResult);
        Assert.IsType<EvaluationResult.Success>(importResult);
    }

    [Fact] // https://github.com/waf/CSharpRepl/issues/355
    public async Task Evaluate_ConflictingPackageVersions_UnifiesToHighestVersion()
    {
        // Two #r closures pull in different versions of the same assembly. Without identity-based unification
        // both land in the compilation, so a type defined in both (JsonConvert) is ambiguous (CS0433) - the same
        // class of failure as the transitive extension-method conflict in the original report (CS1929). Unification
        // collapses them to the highest version, so the code compiles and binds against Newtonsoft.Json 13.
        await services.EvaluateAsync(@"#r ""nuget: Newtonsoft.Json, 12.0.3""", cancellationToken: TestContext.Current.CancellationToken);
        await services.EvaluateAsync(@"#r ""nuget: Newtonsoft.Json, 13.0.3""", cancellationToken: TestContext.Current.CancellationToken);
        var result = await services.EvaluateAsync(
            @"Newtonsoft.Json.JsonConvert.SerializeObject(new { a = 1 }) + "" v"" + typeof(Newtonsoft.Json.JsonConvert).Assembly.GetName().Version",
            cancellationToken: TestContext.Current.CancellationToken);

        var success = Assert.IsType<EvaluationResult.Success>(result);
        Assert.Equal(@"{""a"":1} v13.0.0.0", success.ReturnValue.Value);
    }

    [Fact] // https://github.com/waf/CSharpRepl/issues/355 (runtime side of an #r dll version conflict)
    public async Task Evaluate_ConflictingAssemblyReferenceVersions_BindToHighestAtRuntime()
    {
        // Two #r'd DLLs share an assembly identity at different versions. Compile-time unification
        // (RemoveDuplicateReferences) keeps the highest; at runtime the loader must bind to that same highest
        // version, or the method runs against the lower one. This pins the runtime half of the #r-dll conflict
        // case - today served by unifiedAssemblyPathsByIdentity / the by-name resolver fallback - which has no
        // other automated guard, so the loading-layer refactor can't silently regress it.
        var older = EmitVersionedLib(new Version(1, 0, 0, 0));
        var newer = EmitVersionedLib(new Version(2, 0, 0, 0));

        await services.EvaluateAsync(@$"#r ""{older}""", cancellationToken: TestContext.Current.CancellationToken);
        await services.EvaluateAsync(@$"#r ""{newer}""", cancellationToken: TestContext.Current.CancellationToken);
        var result = await services.EvaluateAsync(
            "RuntimeConflictLib.VersionMarker.Version()",
            cancellationToken: TestContext.Current.CancellationToken);

        var success = Assert.IsType<EvaluationResult.Success>(result);
        Assert.Equal("2.0.0.0", success.ReturnValue.Value);
    }

    /// <summary>
    /// Emits a tiny library whose <c>VersionMarker.Version()</c> reports its own assembly version, so a test can
    /// observe which physical version was bound at run time. Two emits at different versions share one identity.
    /// </summary>
    private static string EmitVersionedLib(Version version)
    {
        var path = Path.Combine(Path.GetTempPath(), $"CSharpRepl_RuntimeConflictLib_{version.ToString().Replace('.', '_')}_{Guid.NewGuid():N}.dll");
        var source =
            $"[assembly: System.Reflection.AssemblyVersion(\"{version}\")] " +
            "namespace RuntimeConflictLib { public static class VersionMarker { " +
            "public static string Version() => typeof(VersionMarker).Assembly.GetName().Version.ToString(); } }";
        var compilation = CSharpCompilation.Create(
            "CSharpRepl_RuntimeConflictLib",
            [CSharpSyntaxTree.ParseText(source)],
            [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)],
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var emit = compilation.Emit(path);
        Assert.True(emit.Success, string.Join(Environment.NewLine, emit.Diagnostics));
        return path;
    }

    [Fact] // https://github.com/waf/CSharpRepl/issues/355
    public async Task Evaluate_TransitiveLowerVersionDependency_BindsToSingleRuntimeInstance()
    {
        // The runtime half of #355, which (unlike Evaluate_ConflictingPackageVersions, the compile-time half) has had
        // no automated guard. Npgsql.EntityFrameworkCore.PostgreSQL 8.0.2's binary references EF Core 8.0.2, but the
        // explicit EF Core 8.0.3 reference unifies the closure upward to 8.0.3 (which is what's on disk). If the
        // scripting loader and the runtime assembly resolver each load their own copy of EF Core, the script's
        // DbContextOptionsBuilder and the one UseNpgsql expects come from two assembly identities and the call throws
        // MissingMethodException at JIT. PinReferencesForRuntimeBinding (one pinned instance) is what makes it bind.
        // This is the heaviest test in the suite - it restores two real package closures.
        await services.EvaluateAsync(@"#r ""nuget: Microsoft.EntityFrameworkCore, 8.0.3""", cancellationToken: TestContext.Current.CancellationToken);
        await services.EvaluateAsync(@"#r ""nuget: Npgsql.EntityFrameworkCore.PostgreSQL, 8.0.2""", cancellationToken: TestContext.Current.CancellationToken);
        await services.EvaluateAsync(@"using Microsoft.EntityFrameworkCore;", cancellationToken: TestContext.Current.CancellationToken);

        // UseNpgsql only configures options (no connection is opened), so this needs no database - but it crosses the
        // EF Core type boundary, which is exactly where a type-identity split surfaces.
        var result = await services.EvaluateAsync(
            @"new DbContextOptionsBuilder().UseNpgsql(""Host=localhost;Database=db;Username=u;Password=p"").Options.Extensions.Any()",
            cancellationToken: TestContext.Current.CancellationToken);

        var success = Assert.IsType<EvaluationResult.Success>(result);
        Assert.Equal(true, success.ReturnValue.Value);

        // T2: the same invariant, asserted directly on the loaded set - exactly one runtime instance per simple name
        // across the whole EF Core / Npgsql closure. A second instance under any name is the split this test guards.
        var splits = LoadedAssemblyInspector.FindSplits(
            name => name.StartsWith("Microsoft.EntityFrameworkCore", StringComparison.Ordinal)
                 || name.StartsWith("Npgsql", StringComparison.Ordinal));
        Assert.True(splits.Count == 0,
            "Expected exactly one runtime instance per assembly, but found a split:" + Environment.NewLine +
            LoadedAssemblyInspector.Describe(
                name => name.StartsWith("Microsoft.EntityFrameworkCore", StringComparison.Ordinal)
                     || name.StartsWith("Npgsql", StringComparison.Ordinal)));
    }

    [Fact] // https://github.com/waf/CSharpRepl/issues/184
    public async Task Evaluate_TransitiveAssemblyReference_IsAccessibleAfterReferencingDependent()
    {
        // A.dll references B.dll, and both sit in the same directory. #r'ing only A should make B's types
        // usable too (csi.exe does this): #r'ing A adds its directory to the search path, so when the
        // compilation binds against the assembly B that A references, B.dll resolves out of that directory.
        var directReferencePath = EmitTransitiveReferenceLibs();

        var referenceResult = await services.EvaluateAsync(@$"#r ""{directReferencePath}""", cancellationToken: TestContext.Current.CancellationToken);
        // The crux of #184: B's namespace must be importable even though only A was #r'd.
        var usingTransitive = await services.EvaluateAsync(@"using TransitiveDependency;", cancellationToken: TestContext.Current.CancellationToken);
        // B's type is then usable directly (constructed here)...
        var useTransitiveType = await services.EvaluateAsync(@"new Widget().Value", cancellationToken: TestContext.Current.CancellationToken);
        // ...and as the return type of A's own public API - the canonical real-world shape of this bug.
        var useViaDirectApi = await services.EvaluateAsync(@"DirectReference.Gadget.MakeWidget().Value", cancellationToken: TestContext.Current.CancellationToken);

        Assert.IsType<EvaluationResult.Success>(referenceResult);
        Assert.True(usingTransitive is EvaluationResult.Success, "using failed: " + Describe(usingTransitive));
        var success = Assert.IsType<EvaluationResult.Success>(useTransitiveType);
        Assert.Equal(42, success.ReturnValue.Value);
        Assert.True(useViaDirectApi is EvaluationResult.Success, "API call failed: " + Describe(useViaDirectApi));
        Assert.Equal(42, ((EvaluationResult.Success)useViaDirectApi).ReturnValue.Value);
    }

    [Fact] // https://github.com/waf/CSharpRepl/issues/184 (runtime half)
    public async Task Evaluate_TransitiveAssemblyReference_LoadsAtRuntimeWhenSignatureHidesIt()
    {
        // A.dll references B.dll (same directory). Gadget.WidgetValue()'s signature returns int (no B type), so
        // the call compiles against A alone - but its body constructs B.Widget, so B.dll must *load* at run time.
        var directReferencePath = EmitTransitiveReferenceLibs();

        var referenceResult = await services.EvaluateAsync(@$"#r ""{directReferencePath}""", cancellationToken: TestContext.Current.CancellationToken);
        var useResult = await services.EvaluateAsync(@"DirectReference.Gadget.WidgetValue()", cancellationToken: TestContext.Current.CancellationToken);

        Assert.IsType<EvaluationResult.Success>(referenceResult);
        Assert.True(useResult is EvaluationResult.Success, "call failed: " + Describe(useResult));
        var success = Assert.IsType<EvaluationResult.Success>(useResult);
        Assert.Equal(42, success.ReturnValue.Value);
    }

    private static string Describe(EvaluationResult result) => result switch
    {
        EvaluationResult.Error e => "ERROR: " + e.Exception,
        EvaluationResult.Success => "SUCCESS",
        _ => result.GetType().Name,
    };

    /// <summary>
    /// Emits two libraries into a shared temp directory: a transitive dependency (assembly
    /// <c>TransitiveDependencyLib</c>, namespace <c>TransitiveDependency</c>, type <c>Widget</c>) and a directly
    /// referenced assembly (<c>DirectReferenceLib</c>, namespace <c>DirectReference</c>) whose public surface uses
    /// <c>Widget</c>. Returns the path to the directly referenced DLL; the dependency sits next to it. The caller
    /// only ever <c>#r</c>'s the returned DLL - the dependency must be discovered transitively (#184).
    /// </summary>
    private static string EmitTransitiveReferenceLibs()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"CSharpRepl_TransitiveRef_{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);

        var corlib = MetadataReference.CreateFromFile(typeof(object).Assembly.Location);

        var dependencyPath = Path.Combine(dir, "TransitiveDependencyLib.dll");
        var dependency = CSharpCompilation.Create(
            "TransitiveDependencyLib",
            [CSharpSyntaxTree.ParseText("namespace TransitiveDependency { public class Widget { public int Value => 42; } }")],
            [corlib],
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var dependencyEmit = dependency.Emit(dependencyPath);
        Assert.True(dependencyEmit.Success, string.Join(Environment.NewLine, dependencyEmit.Diagnostics));

        var directReferencePath = Path.Combine(dir, "DirectReferenceLib.dll");
        var directReference = CSharpCompilation.Create(
            "DirectReferenceLib",
            [CSharpSyntaxTree.ParseText(
                "namespace DirectReference { public static class Gadget { " +
                // MakeWidget's signature exposes B's type -> needs B at *compile* time to even call it.
                "public static TransitiveDependency.Widget MakeWidget() => new TransitiveDependency.Widget(); " +
                // WidgetValue's signature hides B (returns int); its body touches B -> needs B only at *run* time.
                "public static int WidgetValue() => new TransitiveDependency.Widget().Value; } }")],
            [corlib, MetadataReference.CreateFromFile(dependencyPath)],
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var directReferenceEmit = directReference.Emit(directReferencePath);
        Assert.True(directReferenceEmit.Success, string.Join(Environment.NewLine, directReferenceEmit.Diagnostics));

        return directReferencePath;
    }

    [Fact]
    public async Task Evaluate_SolutionReference_ReferencesAllProjects()
    {
        var referenceResult = await services.EvaluateAsync(@"#r ""./Data/DemoSolution/DemoSolution.sln""", cancellationToken: TestContext.Current.CancellationToken);
        var importProject1Result = await services.EvaluateAsync(@"using DemoSolution.DemoProject1;", cancellationToken: TestContext.Current.CancellationToken);
        var importProject2Result = await services.EvaluateAsync(@"using DemoSolution.DemoProject2;", cancellationToken: TestContext.Current.CancellationToken);

        Assert.IsType<EvaluationResult.Success>(referenceResult);
        Assert.IsType<EvaluationResult.Success>(importProject1Result);
        Assert.IsType<EvaluationResult.Success>(importProject2Result);
    }

    [Fact]
    public async Task Evaluate_SolutionReference_ReferencesAllProjects_FromSlnx()
    {
        var referenceResult = await services.EvaluateAsync(@"#r ""./Data/DemoSolution/DemoSolution.slnx""", cancellationToken: TestContext.Current.CancellationToken);
        var importProject1Result = await services.EvaluateAsync(@"using DemoSolution.DemoProject1;", cancellationToken: TestContext.Current.CancellationToken);
        var importProject2Result = await services.EvaluateAsync(@"using DemoSolution.DemoProject2;", cancellationToken: TestContext.Current.CancellationToken);

        Assert.IsType<EvaluationResult.Success>(referenceResult);
        Assert.IsType<EvaluationResult.Success>(importProject1Result);
        Assert.IsType<EvaluationResult.Success>(importProject2Result);
    }

    [Fact]
    public async Task Evaluate_SolutionReference_ReferencesMultipleTargetFrameworks()
    {
        var referenceResult = await services.EvaluateAsync(@"#r ""./Data/ComplexSolution/ComplexSolution.sln""", cancellationToken: TestContext.Current.CancellationToken);
        var importEntryPoint = await services.EvaluateAsync(@"using EntryPoint;", cancellationToken: TestContext.Current.CancellationToken);
        var importLibraryA = await services.EvaluateAsync(@"using LibraryA;", cancellationToken: TestContext.Current.CancellationToken);
        var importLibraryB = await services.EvaluateAsync(@"using LibraryB;", cancellationToken: TestContext.Current.CancellationToken);
        var callResult = await services.EvaluateAsync(@"Program.Main();", cancellationToken: TestContext.Current.CancellationToken);
        // we should be able to import the nuget package dependency from LibraryB.
        var importNugetPackage = await services.EvaluateAsync(@"using Newtonsoft.Json;", cancellationToken: TestContext.Current.CancellationToken);

        Assert.IsType<EvaluationResult.Success>(referenceResult);
        Assert.IsType<EvaluationResult.Success>(importEntryPoint);
        Assert.IsType<EvaluationResult.Success>(importLibraryA);
        Assert.IsType<EvaluationResult.Success>(importLibraryB);
        Assert.IsType<EvaluationResult.Success>(callResult);
        Assert.IsType<EvaluationResult.Success>(importNugetPackage);
    }

    /// <summary>
    /// https://github.com/waf/CSharpRepl/issues/128
    /// </summary>
    [Fact]
    public async Task Evaluate_ResolveCorrectRuntimeVersionOfReferencedAssembly()
    {
        var builder = new DotnetBuilder(console);
        var (buildExitCode, _) = builder.Build("./Data/DemoSolution/DemoSolution.DemoProject3");
        Assert.Equal(0, buildExitCode);

        var referenceResult = await services.EvaluateAsync(@"#r ""./Data/DemoSolution/DemoSolution.DemoProject3/bin/Debug/net10.0/DemoSolution.DemoProject3.dll""", cancellationToken: TestContext.Current.CancellationToken);
        var importResult = await services.EvaluateAsync(@"DemoSolution.DemoProject3.DemoClass3.GetSystemManagementPath()", cancellationToken: TestContext.Current.CancellationToken);

        Assert.IsType<EvaluationResult.Success>(referenceResult);
        Assert.IsType<EvaluationResult.Success>(importResult);

        var referencedSystemManagementPath = (string)((EvaluationResult.Success)importResult).ReturnValue.Value;
        referencedSystemManagementPath = referencedSystemManagementPath.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
        var winRuntimeSelected = referencedSystemManagementPath.Contains(Path.Combine("runtimes", "win", "lib"), StringComparison.OrdinalIgnoreCase);
        var isWin = Environment.OSVersion.Platform == PlatformID.Win32NT;
        Assert.Equal(isWin, winRuntimeSelected);
    }

    /// <summary>
    /// https://github.com/waf/CSharpRepl/issues/318
    /// </summary>
    [Fact]
    public async Task Evaluate_SpanResult()
    {

        var eval1 = await services.EvaluateAsync(@"new[]{1,2,3}.AsSpan()", cancellationToken: TestContext.Current.CancellationToken);
        dynamic r1 = Assert.IsType<EvaluationResult.Success>(eval1).ReturnValue.Value;
        Assert.EndsWith($"{nameof(__CSharpRepl_RuntimeHelper)}+{nameof(__CSharpRepl_RuntimeHelper.SpanOutput)}", r1.GetType().FullName);
        Assert.Equal(3, r1.Count);
        Assert.Equal(typeof(Span<int>), r1.OriginalType);

        var eval2 = await services.EvaluateAsync(@"(ReadOnlySpan<int>)[1,2,3]", cancellationToken: TestContext.Current.CancellationToken);
        dynamic r2 = Assert.IsType<EvaluationResult.Success>(eval2).ReturnValue.Value;
        Assert.EndsWith($"{nameof(__CSharpRepl_RuntimeHelper)}+{nameof(__CSharpRepl_RuntimeHelper.SpanOutput)}", r2.GetType().FullName);
        Assert.Equal(3, r2.Count);
        Assert.Equal(typeof(ReadOnlySpan<int>), r2.OriginalType);

        var eval3 = await services.EvaluateAsync(@"""Hello World"".AsSpan()", cancellationToken: TestContext.Current.CancellationToken);
        dynamic r3 = Assert.IsType<EvaluationResult.Success>(eval3).ReturnValue.Value;
        Assert.EndsWith($"{nameof(__CSharpRepl_RuntimeHelper)}+{nameof(__CSharpRepl_RuntimeHelper.CharSpanOutput)}", r3.GetType().FullName);
        Assert.Equal(11, r3.Count);
        Assert.Equal(typeof(ReadOnlySpan<char>), r3.OriginalType);
    }

    /// <summary>
    /// https://github.com/waf/CSharpRepl/issues/317
    /// </summary>
    [Fact]
    public async Task Evaluate_MemoryResult()
    {

        var eval1 = await services.EvaluateAsync(@"new[]{1,2,3}.AsMemory()", cancellationToken: TestContext.Current.CancellationToken);
        dynamic r1 = Assert.IsType<EvaluationResult.Success>(eval1).ReturnValue.Value;
        Assert.EndsWith($"{nameof(__CSharpRepl_RuntimeHelper)}+{nameof(__CSharpRepl_RuntimeHelper.SpanOutput)}", r1.GetType().FullName);
        Assert.Equal(3, r1.Count);
        Assert.Equal(typeof(Memory<int>), r1.OriginalType);

        var eval2 = await services.EvaluateAsync(@"(ReadOnlyMemory<int>)new[] { 1, 2, 3 }.AsMemory()", cancellationToken: TestContext.Current.CancellationToken);
        dynamic r2 = Assert.IsType<EvaluationResult.Success>(eval2).ReturnValue.Value;
        Assert.EndsWith($"{nameof(__CSharpRepl_RuntimeHelper)}+{nameof(__CSharpRepl_RuntimeHelper.SpanOutput)}", r2.GetType().FullName);
        Assert.Equal(3, r2.Count);
        Assert.Equal(typeof(ReadOnlyMemory<int>), r2.OriginalType);

        var eval3 = await services.EvaluateAsync(@"""Hello World"".AsMemory()", cancellationToken: TestContext.Current.CancellationToken);
        dynamic r3 = Assert.IsType<EvaluationResult.Success>(eval3).ReturnValue.Value;
        Assert.EndsWith($"{nameof(__CSharpRepl_RuntimeHelper)}+{nameof(__CSharpRepl_RuntimeHelper.CharSpanOutput)}", r3.GetType().FullName);
        Assert.Equal(11, r3.Count);
        Assert.Equal(typeof(ReadOnlyMemory<char>), r3.OriginalType);
    }

    /// <summary>
    /// https://github.com/waf/CSharpRepl/issues/318
    /// </summary>
    [Fact]
    public async Task Evaluate_RefStructResult()
    {
        var e1 = await services.EvaluateAsync(@"ref struct S; default(S)", cancellationToken: TestContext.Current.CancellationToken);
        var r1 = Assert.IsType<EvaluationResult.Success>(e1).ReturnValue.Value;
        Assert.EndsWith($"{nameof(__CSharpRepl_RuntimeHelper)}+{nameof(__CSharpRepl_RuntimeHelper.RefStructOutput)}", r1.GetType().FullName);
        Assert.Equal($"Cannot output a value of 'S' because it's a ref-struct. It has to override ToString() to see its value.", r1.ToString());

        var e2 = await services.EvaluateAsync(@"ref struct S{public override string ToString()=>""custom result"";} default(S)", cancellationToken: TestContext.Current.CancellationToken);
        var r2 = Assert.IsType<EvaluationResult.Success>(e2).ReturnValue.Value;
        Assert.EndsWith($"{nameof(__CSharpRepl_RuntimeHelper)}+{nameof(__CSharpRepl_RuntimeHelper.RefStructOutput)}", r2.GetType().FullName);
        Assert.Equal("custom result", r2.ToString());
    }
}
