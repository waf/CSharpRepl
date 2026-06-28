// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using CSharpRepl.InjectedHook.Contracts;
using CSharpRepl.InjectedHook.ScriptEngine;
using Xunit;

namespace CSharpRepl.Tests;

/// <summary>
/// Integration tests for the injected Roslyn engine, hosted in-process: the engine builds its compilation
/// references from whatever process hosts it, so running it inside the test process exercises the same real
/// CSharpScript pipeline as inside a hooked target — submissions compile against this process's loaded
/// assemblies and bind to its live objects (see <see cref="EngineTestProbe"/>). The cross-process transport
/// and server around the engine are covered by the hooked-child-process tests.
/// </summary>
public class ConnectorEngineTests : IClassFixture<ConnectorEngineTests.EngineFixture>
{
    /// <summary>
    /// One engine for the whole class: its submissions share a persisted state chain (like a real session),
    /// so tests use distinct identifiers. Tests within a class run serially, matching the engine's one-at-a-time
    /// evaluation gate.
    /// </summary>
    public sealed class EngineFixture
    {
        public ConnectorEngine Engine { get; } = new();
    }

    private readonly ConnectorEngine engine;

    public ConnectorEngineTests(EngineFixture fixture) => this.engine = fixture.Engine;

    private Task<EvalResponse> EvalAsync(string code, bool detailed = false) =>
        engine.EvalAsync(code, detailed, TestContext.Current.CancellationToken);

    [Fact]
    public async Task EvalChain_BindsLiveStatics_AndSurvivesFailedSubmissions()
    {
        // A write through the engine lands on the real static in the hosting process — the same live-object
        // binding a hooked target relies on (in-process analogue of the cross-process write-back test).
        var write = await EvalAsync("CSharpRepl.Tests.EngineTestProbe.WriteProbe = 1234;");
        Assert.Equal(ResultKind.Void, write.Kind);
        Assert.True(write.Committed);
        Assert.Equal(1234, EngineTestProbe.WriteProbe);

        var read = await EvalAsync("CSharpRepl.Tests.EngineTestProbe.Numbers.Sum()");
        Assert.Equal("6", read.Value!.DisplayText);

        // Set up chain state, then fail both ways; the chain must survive untouched.
        Assert.True((await EvalAsync("var chained = 21;")).Committed);

        var compileError = await EvalAsync("var oops = definitelyNotDefined + 1;");
        Assert.Equal(ResultKind.Exception, compileError.Kind);
        Assert.False(compileError.Committed);
        Assert.Equal("CompilationError", compileError.Exception!.TypeName);
        Assert.Contains("CS0103", compileError.Exception.Message);

        var runtimeError = await EvalAsync("""var ghost = 1; throw new InvalidOperationException("kaboom");""");
        Assert.Equal(ResultKind.Exception, runtimeError.Kind);
        Assert.False(runtimeError.Committed);
        Assert.Equal("System.InvalidOperationException", runtimeError.Exception!.TypeName);
        Assert.Equal("kaboom", runtimeError.Exception.Message);
        Assert.Contains("InvalidOperationException", runtimeError.Exception.Detail);

        // The throwing submission did not commit: its local never came into scope...
        var ghost = await EvalAsync("ghost");
        Assert.Equal(ResultKind.Exception, ghost.Kind);
        Assert.Contains("CS0103", ghost.Exception!.Message);

        // ...while state from before the failures is still usable.
        var chained = await EvalAsync("chained * 2");
        Assert.Equal("42", chained.Value!.DisplayText);
    }

    [Fact]
    public async Task Eval_DistinguishesValuesFromVoid_LikeTheLocalRepl()
    {
        Assert.Equal(ResultKind.Value, (await EvalAsync("Math.Max(100, 1)")).Kind);

        // A trailing semicolon makes the same expression a statement — void, exactly like the local REPL.
        Assert.Equal(ResultKind.Void, (await EvalAsync("Math.Max(100, 1);")).Kind);

        // Declarations are void, and the declared method persists for the next submission.
        Assert.Equal(ResultKind.Void, (await EvalAsync("int Cube(int n) => n * n * n;")).Kind);
        var cubed = await EvalAsync("Cube(3)");
        Assert.Equal(ResultKind.Value, cubed.Kind);
        Assert.Equal("27", cubed.Value!.DisplayText);

        // A void-returning call is void even without the semicolon...
        Assert.Equal(ResultKind.Void, (await EvalAsync("System.Threading.Thread.Sleep(0)")).Kind);

        // ...but a null-returning expression is a value (null), not void.
        var nullValue = await EvalAsync("(string)null");
        Assert.Equal(ResultKind.Value, nullValue.Kind);
        Assert.True(nullValue.Value!.IsNull);
        Assert.Equal(RemoteValueKind.Null, nullValue.Value.Kind);
        Assert.Equal("null", nullValue.Value.DisplayText);
        Assert.Equal(RemoteValueStyle.Keyword, nullValue.Value.Style);
    }

    [Fact]
    public async Task Projection_FormatsScalarsAndCollections_LikeTheLocalRepl()
    {
        var dayOfWeek = (await EvalAsync("DayOfWeek.Friday")).Value!;
        Assert.Equal(RemoteValueKind.Scalar, dayOfWeek.Kind);
        Assert.Equal("Friday", dayOfWeek.DisplayText);
        Assert.Equal("DayOfWeek", dayOfWeek.TypeName);

        var newline = (await EvalAsync("'\\n'")).Value!;
        Assert.Equal(@"'\n'", newline.DisplayText); // escaped, matching the local PrimitiveFormatter
        Assert.Equal(RemoteValueStyle.String, newline.Style);

        var boolean = (await EvalAsync("true")).Value!;
        Assert.Equal("true", boolean.DisplayText);
        Assert.Equal(RemoteValueStyle.Keyword, boolean.Style);

        // Numbers are invariant-culture, regardless of the test machine's locale.
        var floating = (await EvalAsync("3.5 + 0.25")).Value!;
        Assert.Equal("3.75", floating.DisplayText);
        Assert.Equal(RemoteValueStyle.Number, floating.Style);
        Assert.Equal("double", floating.TypeName);

        var escaped = (await EvalAsync(""" "a\"b\nc" """)).Value!;
        Assert.Equal("\"a\\\"b\\nc\"", escaped.DisplayText);

        // An object with no ToString override falls back to its friendly type name.
        var plainObject = (await EvalAsync("new object()")).Value!;
        Assert.Equal(RemoteValueKind.Object, plainObject.Kind);
        Assert.Equal("object", plainObject.DisplayText);
        Assert.Equal(RemoteValueStyle.TypeName, plainObject.Style);

        // Friendly type names: array ranks and generic arguments.
        var rectangular = (await EvalAsync("new int[2, 3]")).Value!;
        Assert.Equal(RemoteValueKind.Collection, rectangular.Kind);
        Assert.Equal("int[,]", rectangular.TypeName);
        Assert.Equal(6, rectangular.Count);

        var dictionary = (await EvalAsync("""new Dictionary<string, int> { ["a"] = 1 }""")).Value!;
        Assert.Equal("Dictionary<string, int>", dictionary.TypeName);
        var pair = Assert.Single(dictionary.Items!);
        Assert.Equal("[a, 1]", pair.DisplayText); // KeyValuePair's own ToString as the object summary

        // Breadth limit: elements are capped at 100, but the collection's true count is preserved.
        var big = (await EvalAsync("Enumerable.Range(0, 250).ToArray()")).Value!;
        Assert.Equal("int[]", big.TypeName);
        Assert.Equal(100, big.Items!.Count);
        Assert.Equal(250, big.Count);
        Assert.True(big.Truncated);

        // A throwing enumerator keeps what was already projected and marks the result truncated.
        await EvalAsync("""IEnumerable<int> Halts() { yield return 1; yield return 2; throw new InvalidOperationException("stop"); }""");
        var halted = (await EvalAsync("Halts()")).Value!;
        Assert.Equal(["1", "2"], halted.Items!.Select(i => i.DisplayText));
        Assert.True(halted.Truncated);
    }

    [Fact]
    public async Task DetailedProjection_ReflectsMembers_WithDepthAndFailureLimits()
    {
        var declare = await EvalAsync("""
            class ProjectionChild { public int Inner { get; } = 5; }
            class ProjectionSample
            {
                public int Number { get; } = 7;
                public string Text { get; } = "hi";
                public List<int> Nested { get; } = new() { 1, 2, 3 };
                public ProjectionChild Child { get; } = new();
                public int Throws => throw new InvalidOperationException("nope");
                public int Exposed = 99;
                private int Hidden { get; } = 1;
            }
            """);
        Assert.Equal(ResultKind.Void, declare.Kind);

        // The simple view never reflects members, so property getters aren't invoked for a one-line summary.
        var simple = (await EvalAsync("new ProjectionSample()")).Value!;
        Assert.Equal(RemoteValueKind.Object, simple.Kind);
        Assert.Null(simple.Members);
        Assert.Equal("ProjectionSample", simple.DisplayText); // no ToString override → type-name summary

        var detailed = (await EvalAsync("new ProjectionSample()", detailed: true)).Value!;
        Assert.NotNull(detailed.Members);

        // Public properties and fields only (no privates), sorted by name.
        Assert.Equal(
            ["Child", "Exposed", "Nested", "Number", "Text", "Throws"],
            detailed.Members!.Select(m => m.Name));

        Assert.Equal("7", detailed.Members.Single(m => m.Name == "Number").Value.DisplayText);
        Assert.Equal("\"hi\"", detailed.Members.Single(m => m.Name == "Text").Value.DisplayText);
        Assert.Equal("99", detailed.Members.Single(m => m.Name == "Exposed").Value.DisplayText);

        // Depth limit: a member collection is a type-name + count header with no elements...
        var nested = detailed.Members.Single(m => m.Name == "Nested").Value;
        Assert.Equal(RemoteValueKind.Collection, nested.Kind);
        Assert.Null(nested.Items);
        Assert.Equal(3, nested.Count);

        // ...and a member object is a summary with no members of its own.
        var child = detailed.Members.Single(m => m.Name == "Child").Value;
        Assert.Equal(RemoteValueKind.Object, child.Kind);
        Assert.Null(child.Members);

        // A throwing getter is surfaced per-member instead of sinking the whole projection.
        Assert.Equal("!<InvalidOperationException>", detailed.Members.Single(m => m.Name == "Throws").Value.DisplayText);
    }

    [Fact]
    public async Task ReplaceMethod_DetoursLiveMethods_AndRevertsThem()
    {
        var ct = TestContext.Current.CancellationToken;
        try
        {
            // 1. Static method, replaced via a delegate value the user defined in the REPL.
            Assert.Equal(15, EngineTestProbe.Triple(5)); // original behavior
            Assert.True((await EvalAsync("System.Func<int, int> tripler100 = x => x * 100;")).Committed);
            var staticReplace = await engine.ReplaceMethodAsync(
                "CSharpRepl.Tests.EngineTestProbe.Triple", "tripler100", PatchMode.Replace, ct);
            Assert.True(staticReplace.Ok, staticReplace.Error);
            Assert.Equal(500, EngineTestProbe.Triple(5)); // now detoured to the REPL delegate

            // 2. Instance method, whose replacement reads live instance state (self.Factor).
            var sample = new PatchSample { Factor = 10 };
            Assert.Equal(50, sample.Scale(5)); // original: value * Factor
            Assert.True((await EvalAsync(
                "System.Func<CSharpRepl.Tests.PatchSample, int, int> scaler = (self, v) => v * self.Factor + 1;")).Committed);
            var instanceReplace = await engine.ReplaceMethodAsync(
                "CSharpRepl.Tests.PatchSample.Scale", "scaler", PatchMode.Replace, ct);
            Assert.True(instanceReplace.Ok, instanceReplace.Error);
            Assert.Equal(51, sample.Scale(5)); // detoured: 5 * 10 + 1

            // 3. Both patches are tracked.
            var list = await engine.ListPatchesAsync(ct);
            Assert.Equal(2, list.Patches.Count);

            // 4. Restoring one returns its original behavior; the other stays patched.
            Assert.True((await engine.RevertAsync(staticReplace.PatchId, all: false, ct)).Ok);
            Assert.Equal(15, EngineTestProbe.Triple(5));
            Assert.Equal(51, sample.Scale(5));

            // 5. A bare named method (a method group, not a value) is coerced via the generated delegate cast.
            Assert.True((await EvalAsync("int Quad(int v) => v * 4;")).Committed);
            var namedReplace = await engine.ReplaceMethodAsync(
                "CSharpRepl.Tests.EngineTestProbe.Triple", "Quad", PatchMode.Replace, ct);
            Assert.True(namedReplace.Ok, namedReplace.Error);
            Assert.Equal(20, EngineTestProbe.Triple(5));
            await engine.RevertAsync(namedReplace.PatchId, all: false, ct);

            // 6. Wrap mode: the replacement calls the original through the leading "orig" delegate.
            Assert.True((await EvalAsync(
                "System.Func<System.Func<int, int>, int, int> plusOne = (orig, v) => orig(v) + 1;")).Committed);
            var wrap = await engine.ReplaceMethodAsync(
                "CSharpRepl.Tests.EngineTestProbe.Triple", "plusOne", PatchMode.Wrap, ct);
            Assert.True(wrap.Ok, wrap.Error);
            Assert.Equal(16, EngineTestProbe.Triple(5)); // original 15, + 1

            // 7. A signature that matches no overload is reported, not thrown.
            Assert.True((await EvalAsync("System.Func<string, string> wrongSig = s => s;")).Committed);
            var mismatch = await engine.ReplaceMethodAsync(
                "CSharpRepl.Tests.EngineTestProbe.Triple", "wrongSig", PatchMode.Replace, ct);
            Assert.False(mismatch.Ok);
            Assert.NotNull(mismatch.Error);

            // An unknown target is also a clean failure.
            var unknown = await engine.ReplaceMethodAsync(
                "CSharpRepl.Tests.EngineTestProbe.NoSuchMethod", "tripler100", PatchMode.Replace, ct);
            Assert.False(unknown.Ok);
        }
        finally
        {
            // Detours are process-global, so never leak one into another test in this process.
            await engine.RevertAsync(0, all: true, ct);
        }

        Assert.Empty((await engine.ListPatchesAsync(ct)).Patches);
        Assert.Equal(15, EngineTestProbe.Triple(5)); // fully reverted
    }

    [Fact]
    public async Task ReplaceMethod_SupportsRefAndOutParameters_ViaNamedMethods()
    {
        var ct = TestContext.Current.CancellationToken;
        try
        {
            // out, static — defined as a named method (the realistic UX, which Func can't express).
            Assert.True(RefProbe.TryDouble(5, out var d0));
            Assert.Equal(10, d0);
            Assert.True((await EvalAsync(
                "bool MyTryDouble(int input, out int result) { result = 999; return false; }")).Committed);
            var outStatic = await engine.ReplaceMethodAsync(
                "CSharpRepl.Tests.RefProbe.TryDouble", "MyTryDouble", PatchMode.Replace, ct);
            Assert.True(outStatic.Ok, outStatic.Error);
            Assert.False(RefProbe.TryDouble(5, out var d1)); // detoured: returns false, out = 999
            Assert.Equal(999, d1);

            // ref, static.
            Assert.True((await EvalAsync("void MyBump(ref int value) { value += 100; }")).Committed);
            var refStatic = await engine.ReplaceMethodAsync(
                "CSharpRepl.Tests.RefProbe.Bump", "MyBump", PatchMode.Replace, ct);
            Assert.True(refStatic.Ok, refStatic.Error);
            var bumped = 1;
            RefProbe.Bump(ref bumped);
            Assert.Equal(101, bumped); // detoured: += 100 instead of += 1

            // out, instance — the replacement reads live instance state (self.Factor).
            var sample = new RefSample { Factor = 10 };
            Assert.True(sample.TryScale(5, out var s0));
            Assert.Equal(50, s0);
            Assert.True((await EvalAsync(
                "bool MyTryScale(CSharpRepl.Tests.RefSample self, int input, out int result) { result = input * self.Factor + 1; return false; }")).Committed);
            var outInstance = await engine.ReplaceMethodAsync(
                "CSharpRepl.Tests.RefSample.TryScale", "MyTryScale", PatchMode.Replace, ct);
            Assert.True(outInstance.Ok, outInstance.Error);
            Assert.False(sample.TryScale(5, out var s1)); // detoured: 5 * 10 + 1
            Assert.Equal(51, s1);

            // A generic-method target is still excluded cleanly (out of scope, no crash).
            Assert.True((await EvalAsync("int Identity(int v) => v;")).Committed);
            var generic = await engine.ReplaceMethodAsync(
                "CSharpRepl.Tests.RefProbe.Echo", "Identity", PatchMode.Replace, ct);
            Assert.False(generic.Ok);

            // Revert everything; the originals come back, by-ref behavior included.
            await engine.RevertAsync(0, all: true, ct);
            Assert.True(RefProbe.TryDouble(5, out var d2));
            Assert.Equal(10, d2);
            var bumped2 = 1;
            RefProbe.Bump(ref bumped2);
            Assert.Equal(2, bumped2);
            Assert.True(sample.TryScale(5, out var s2));
            Assert.Equal(50, s2);
        }
        finally
        {
            await engine.RevertAsync(0, all: true, ct);
        }
    }

    [Fact]
    public async Task ReplaceMethod_CoercesOverloadedMethodGroups_ViaGeneratedDelegateCast()
    {
        // A *single* named method has a natural delegate type (C# 10+), so it evaluates as a value and takes the
        // delegate-value path. An *overloaded* method group has no natural type, so it can only be coerced by
        // casting it to the delegate each candidate expects — the engine's method-group path, which builds the
        // cast (ConnectorPatcher.BuildCastDelegate / BuildParameterList / ParameterText / DelegateTypeText).
        var ct = TestContext.Current.CancellationToken;
        try
        {
            // Static, by value → Replace-mode cast emits a delegate TYPE, BuildParameterList renders a static
            // (no instance) parameter list.
            Assert.True((await EvalAsync("int Septuple(int v) => v * 7; int Septuple(int v, int w) => v + w;")).Committed);
            var staticReplace = await engine.ReplaceMethodAsync(
                "CSharpRepl.Tests.EngineTestProbe.Triple", "Septuple", PatchMode.Replace, ct);
            Assert.True(staticReplace.Ok, staticReplace.Error);
            Assert.Equal(35, EngineTestProbe.Triple(5)); // 5 * 7

            // Instance, by value → BuildParameterList prepends the declaring type as the first (__self) parameter.
            var sample = new PatchSample { Factor = 10 };
            Assert.True((await EvalAsync(
                "int ScaleC(CSharpRepl.Tests.PatchSample self, int v) => v * self.Factor + 2; int ScaleC(CSharpRepl.Tests.PatchSample self, int v, int w) => 0;")).Committed);
            var instanceReplace = await engine.ReplaceMethodAsync(
                "CSharpRepl.Tests.PatchSample.Scale", "ScaleC", PatchMode.Replace, ct);
            Assert.True(instanceReplace.Ok, instanceReplace.Error);
            Assert.Equal(52, sample.Scale(5)); // 5 * 10 + 2

            // out parameter → ParameterText renders the "out" modifier on the emitted delegate type.
            Assert.True((await EvalAsync(
                "bool TryC(int input, out int result) { result = 777; return false; } bool TryC(int input, out int result, int extra) { result = 0; return false; }")).Committed);
            var outReplace = await engine.ReplaceMethodAsync(
                "CSharpRepl.Tests.RefProbe.TryDouble", "TryC", PatchMode.Replace, ct);
            Assert.True(outReplace.Ok, outReplace.Error);
            Assert.False(RefProbe.TryDouble(5, out var outValue));
            Assert.Equal(777, outValue);

            // ref parameter → ParameterText renders the "ref" modifier.
            Assert.True((await EvalAsync(
                "void BumpC(ref int value) { value += 100; } void BumpC(ref int value, int extra) { }")).Committed);
            var refReplace = await engine.ReplaceMethodAsync(
                "CSharpRepl.Tests.RefProbe.Bump", "BumpC", PatchMode.Replace, ct);
            Assert.True(refReplace.Ok, refReplace.Error);
            var bumped = 1;
            RefProbe.Bump(ref bumped);
            Assert.Equal(101, bumped); // += 100 instead of += 1
        }
        finally
        {
            await engine.RevertAsync(0, all: true, ct);
        }
    }

    [Fact]
    public async Task ReplaceMethod_OverloadedMethodGroups_WrapAndRejectUnsupportedShapes()
    {
        var ct = TestContext.Current.CancellationToken;
        try
        {
            // Wrap with an overloaded (non-value) method group → the cast builds the shared "orig" delegate
            // type (DelegateTypeText): a Func for a value-returning target...
            Assert.True((await EvalAsync(
                "int WrapC(System.Func<int, int> orig, int v) => orig(v) + 5; int WrapC(System.Func<int, int> orig, int v, int w) => 0;")).Committed);
            var wrapFunc = await engine.ReplaceMethodAsync(
                "CSharpRepl.Tests.EngineTestProbe.Triple", "WrapC", PatchMode.Wrap, ct);
            Assert.True(wrapFunc.Ok, wrapFunc.Error);
            Assert.Equal(20, EngineTestProbe.Triple(5)); // original 15, + 5
            await engine.RevertAsync(wrapFunc.PatchId, all: false, ct);

            // ...and an Action for a void target.
            Assert.True((await EvalAsync(
                "void PokeC(System.Action<int> orig, int n) => orig(n + 1000); void PokeC(System.Action<int> orig, int n, int x) { }")).Committed);
            var wrapAction = await engine.ReplaceMethodAsync(
                "CSharpRepl.Tests.CoercionProbe.Poke", "PokeC", PatchMode.Wrap, ct);
            Assert.True(wrapAction.Ok, wrapAction.Error);
            CoercionProbe.Poke(9);
            Assert.Equal(1009, CoercionProbe.LastPoke); // wrapper added 1000 before calling the original
            await engine.RevertAsync(wrapAction.PatchId, all: false, ct);

            // Array and generic parameter types render through the cast's compilable type-name builder.
            Assert.True((await EvalAsync(
                "int MixedC(int[] xs, System.Collections.Generic.List<int> ys) => 42; int MixedC(int[] xs, System.Collections.Generic.List<int> ys, int z) => 0;")).Committed);
            var mixed = await engine.ReplaceMethodAsync(
                "CSharpRepl.Tests.CoercionProbe.Mixed", "MixedC", PatchMode.Replace, ct);
            Assert.True(mixed.Ok, mixed.Error);
            Assert.Equal(42, CoercionProbe.Mixed([1, 2], [3]));
            await engine.RevertAsync(mixed.PatchId, all: false, ct);

            // Shapes the cast can't express are declined cleanly (it returns null, so no overload matches and the
            // engine reports a failure instead of throwing): a by-ref return can't be a delegate...
            Assert.True((await EvalAsync("int AnyC(int a) => a; int AnyC(int a, int b) => a + b;")).Committed);
            var byRefReturn = await engine.ReplaceMethodAsync(
                "CSharpRepl.Tests.CoercionProbe.RefReturn", "AnyC", PatchMode.Replace, ct);
            Assert.False(byRefReturn.Ok);

            // ...and a wrap over a by-ref parameter can't share a Func/Action "orig" type.
            Assert.True((await EvalAsync("void WrapRefC(int a) { } void WrapRefC(int a, int b) { }")).Committed);
            var wrapByRef = await engine.ReplaceMethodAsync(
                "CSharpRepl.Tests.RefProbe.Bump", "WrapRefC", PatchMode.Wrap, ct);
            Assert.False(wrapByRef.Ok);
        }
        finally
        {
            await engine.RevertAsync(0, all: true, ct);
        }
    }

    [Fact]
    public async Task GetReferencePaths_ReportsTheHostProcessesLoadedAssemblies()
    {
        var paths = await engine.GetReferencePathsAsync(TestContext.Current.CancellationToken);

        Assert.NotEmpty(paths);
        Assert.Contains(typeof(object).Assembly.Location, paths);
        Assert.Contains(typeof(ConnectorEngineTests).Assembly.Location, paths);
        Assert.All(paths, p => Assert.True(File.Exists(p), $"Reported reference path does not exist: '{p}'"));
    }
}

/// <summary>
/// Mutable static state in the test assembly for <see cref="ConnectorEngineTests"/> submissions to bind to —
/// the in-process stand-in for the hooked target's <c>Program.WriteProbe</c>-style statics.
/// </summary>
public static class EngineTestProbe
{
    public static int WriteProbe;
    public static readonly int[] Numbers = [1, 2, 3];

    // A patchable static method for the method-replacement tests. NoInlining so the JIT keeps a real call site
    // for the detour to repoint (an inlined copy wouldn't see the patch).
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int Triple(int value) => value * 3;
}

/// <summary>An instance type with patchable behavior that depends on live instance state.</summary>
public sealed class PatchSample
{
    public int Factor = 10;

    [MethodImpl(MethodImplOptions.NoInlining)]
    public int Scale(int value) => value * Factor;
}

/// <summary>Static targets with by-ref parameters (out/ref) plus a generic method (excluded from patching).</summary>
public static class RefProbe
{
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static bool TryDouble(int input, out int result) { result = input * 2; return true; }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void Bump(ref int value) => value += 1;

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static T Echo<T>(T value) => value;
}

/// <summary>An instance type with an out-parameter method that reads live instance state.</summary>
public sealed class RefSample
{
    public int Factor = 10;

    [MethodImpl(MethodImplOptions.NoInlining)]
    public bool TryScale(int input, out int result) { result = input * Factor; return true; }
}

/// <summary>
/// Targets for the method-group coercion path: array/generic parameter rendering, a void target (so a wrap
/// produces an Action orig delegate), and a by-ref return (a shape the cast can't express).
/// </summary>
public static class CoercionProbe
{
    public static int LastPoke;
    private static int refCell = 7;

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int Mixed(int[] xs, System.Collections.Generic.List<int> ys) => xs.Length + ys.Count;

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void Poke(int n) => LastPoke = n;

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static ref int RefReturn() => ref refCell;
}
