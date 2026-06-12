// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.IO;
using System.Linq;
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
public class InspectorEngineTests : IClassFixture<InspectorEngineTests.EngineFixture>
{
    /// <summary>
    /// One engine for the whole class: its submissions share a persisted state chain (like a real session),
    /// so tests use distinct identifiers. Tests within a class run serially, matching the engine's one-at-a-time
    /// evaluation gate.
    /// </summary>
    public sealed class EngineFixture
    {
        public InspectorEngine Engine { get; } = new();
    }

    private readonly InspectorEngine engine;

    public InspectorEngineTests(EngineFixture fixture) => this.engine = fixture.Engine;

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
    public async Task GetReferencePaths_ReportsTheHostProcessesLoadedAssemblies()
    {
        var paths = await engine.GetReferencePathsAsync(TestContext.Current.CancellationToken);

        Assert.NotEmpty(paths);
        Assert.Contains(typeof(object).Assembly.Location, paths);
        Assert.Contains(typeof(InspectorEngineTests).Assembly.Location, paths);
        Assert.All(paths, p => Assert.True(File.Exists(p), $"Reported reference path does not exist: '{p}'"));
    }
}

/// <summary>
/// Mutable static state in the test assembly for <see cref="InspectorEngineTests"/> submissions to bind to —
/// the in-process stand-in for the hooked target's <c>Program.WriteProbe</c>-style statics.
/// </summary>
public static class EngineTestProbe
{
    public static int WriteProbe;
    public static readonly int[] Numbers = [1, 2, 3];
}
