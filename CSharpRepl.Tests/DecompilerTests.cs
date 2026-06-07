using System.Linq;
using System.Threading.Tasks;
using CSharpRepl.Services;
using CSharpRepl.Services.Roslyn;
using CSharpRepl.Services.Roslyn.Scripting;
using PrettyPrompt.Highlighting;
using Xunit;

namespace CSharpRepl.Tests;

[Collection(nameof(RoslynServices))]
public class DecompilerTests : IAsyncLifetime
{
    private readonly RoslynServices services;

    public DecompilerTests()
    {
        var console = FakeConsole.Create(width: 200);
        this.services = new RoslynServices(console, new Configuration(), new TestTraceLogger());
    }

    public ValueTask InitializeAsync() => new(services.WarmUpAsync([]));
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task Decompile_AsyncCode_ShowsLoweredStateMachine()
    {
        // top-level await turns the entry point into an async method, which the compiler lowers into a state machine.
        var result = await services.ConvertToLoweredCSharp("await Task.Delay(1);", debugMode: true);

        var success = Assert.IsType<EvaluationResult.Success>(result);
        var output = success.ReturnValue.ToString();

        // with the async/await reconstruction pass disabled, the generated state machine is shown explicitly.
        Assert.Contains("IAsyncStateMachine", output);
        Assert.Contains("MoveNext", output);
    }

    [Fact]
    public async Task Decompile_Foreach_IsLoweredToEnumeratorLoop()
    {
        // foreach over a List (rather than an array, which the compiler lowers to an index loop) exercises the
        // GetEnumerator/MoveNext pattern.
        var result = await services.ConvertToLoweredCSharp("foreach (var i in new List<int> { 1, 2, 3 }) { System.Console.WriteLine(i); }", debugMode: true);

        var success = Assert.IsType<EvaluationResult.Success>(result);
        var output = success.ReturnValue.ToString();

        // the foreach reconstruction pass is disabled, so the enumerator pattern is shown explicitly.
        Assert.Contains("MoveNext", output);
        Assert.DoesNotContain("foreach", output);
    }

    [Fact]
    public async Task Decompile_InputAcrossMultipleReplLines_CanDecompile()
    {
        // define a variable
        await services.EvaluateAsync("var x = 5;", cancellationToken: TestContext.Current.CancellationToken);

        // decompile code that uses the above variable. The roslyn scripting host promotes the local to a field,
        // so this exercises the "Scripting session" compiler fallback.
        var result = await services.ConvertToLoweredCSharp("System.Console.WriteLine(x);", debugMode: false);

        var success = Assert.IsType<EvaluationResult.Success>(result);
        Assert.Contains("Compiling code as Scripting session (will be overly verbose): succeeded", success.ReturnValue.ToString());
    }

    [Fact]
    public async Task Decompile_ReportsOptimizationMode()
    {
        var debug = await services.ConvertToLoweredCSharp("var x = 5;", debugMode: true);
        Assert.Contains("// Lowered in Debug Mode.", Assert.IsType<EvaluationResult.Success>(debug).ReturnValue.ToString());

        var release = await services.ConvertToLoweredCSharp("var x = 5;", debugMode: false);
        Assert.Contains("// Lowered in Release Mode.", Assert.IsType<EvaluationResult.Success>(release).ReturnValue.ToString());
    }

    [Fact]
    public async Task Decompile_ProducesValidHighlightSpans()
    {
        var result = await services.ConvertToLoweredCSharp("var x = 5;", debugMode: true);

        var success = Assert.IsType<EvaluationResult.Success>(result);
        var formatted = Assert.IsType<FormattedString>(success.ReturnValue.Value);
        var text = formatted.Text!;
        var spans = formatted.FormatSpans.ToArray();

        Assert.NotEmpty(spans);

        // every span must be in-bounds...
        Assert.All(spans, s => Assert.True(s.Start >= 0 && s.Start + s.Length <= text.Length));

        // ...and non-overlapping when sorted, which the ANSI renderer requires.
        var sorted = spans.OrderBy(s => s.Start).ToArray();
        for (var i = 1; i < sorted.Length; i++)
        {
            Assert.True(sorted[i - 1].Start + sorted[i - 1].Length <= sorted[i].Start, "highlight spans must not overlap");
        }
    }

    [Fact]
    public async Task Decompile_OmitsAssemblyAndModuleAttributeBoilerplate()
    {
        var result = await services.ConvertToLoweredCSharp("System.Console.WriteLine(1);", debugMode: true);

        var output = Assert.IsType<EvaluationResult.Success>(result).ReturnValue.ToString();

        // the [assembly: ...] / [module: ...] boilerplate ILSpy emits for a whole-module decompile is stripped.
        Assert.DoesNotContain("[assembly:", output);
        Assert.DoesNotContain("[module:", output);
    }

    [Fact]
    public async Task Decompile_KeepsStatementsOnASingleLine()
    {
        var result = await services.ConvertToLoweredCSharp("foreach (var x in new[] { 1, 2, 3 }) { System.Console.WriteLine(x); }", debugMode: true);

        var output = Assert.IsType<EvaluationResult.Success>(result).ReturnValue.ToString();

        // regression: ILSpy's "Allman" preset wraps long expressions mid-line; the default options we use keep
        // the array initializer and the for-statement header each on one line.
        Assert.Contains("new int[3] { 1, 2, 3 }", output);
        Assert.Contains("for (int i = 0; i < array.Length; i++)", output);
    }

    [Fact]
    public async Task Decompile_UsesFourSpaceIndentation()
    {
        var result = await services.ConvertToLoweredCSharp("System.Console.WriteLine(1);", debugMode: true);

        var output = Assert.IsType<EvaluationResult.Success>(result).ReturnValue.ToString();

        // members are indented with four spaces, not tabs.
        Assert.Contains("\n    private static", output.Replace("\r\n", "\n"));
        Assert.DoesNotContain("\t", output);
    }

    [Fact]
    public async Task Decompile_UsesUnixLineEndings()
    {
        var result = await services.ConvertToLoweredCSharp("System.Console.WriteLine(1);", debugMode: true);

        var output = Assert.IsType<EvaluationResult.Success>(result).ReturnValue.ToString();

        // regression: ILSpy emits '\r\n', but the ANSI renderer advances lines itself, so a stray '\r\n'
        // double-spaces the output. The text must use '\n'-only line endings (like the IL disassembler).
        Assert.DoesNotContain('\r', output!);
        Assert.Contains('\n', output);
    }

    [Fact]
    public async Task Decompile_InvalidCode_ReturnsError()
    {
        var result = await services.ConvertToLoweredCSharp("this is not valid c#", debugMode: true);

        var error = Assert.IsType<EvaluationResult.Error>(result);
        Assert.Contains("Could not compile provided code", error.Exception.Message);
    }
}
