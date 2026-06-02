// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Threading;
using System.Threading.Tasks;
using BenchmarkDotNet.Attributes;
using CSharpRepl.Services;
using CSharpRepl.Services.Roslyn;
using CSharpRepl.Services.Roslyn.Scripting;
using PrettyPrompt.Consoles;

namespace CSharpRepl.Benchmarks;

/// <summary>
/// Measures the Roslyn-backed callbacks that PrettyPrompt invokes on the input loop. The loop is serial:
/// each keystroke is processed to completion (see PrettyPrompt's Prompt.ReadLineAsync) before the next key
/// is read, so the wall-clock here is exactly the input latency a keystroke pays — there is no per-keystroke
/// cancellation, the token threaded into these callbacks is always CancellationToken.None.
///
/// <para><see cref="PriorSubmissions"/> grows the linked list of Roslyn projects (each evaluation appends a
/// project with a ProjectReference to the previous one — see WorkspaceManager), letting us see whether
/// per-keystroke cost scales with how long the session has been running.</para>
///
/// Run with: dotnet run -c Release --project CSharpRepl.Benchmarks -- --filter *PerKeystroke*
/// </summary>
[MemoryDiagnoser]
[SimpleJob(launchCount: 1, warmupCount: 3, iterationCount: 5)]
public class PerKeystrokeBenchmark
{
    [Params(0, 10, 50)]
    public int PriorSubmissions;

    // A representative line mid-typing: a member-access that forces real semantic binding.
    // The caret sits right after the partial member name, as it would while typing.
    private const string TypedLine = "System.Console.Wri";
    private const int Caret = 18; // == TypedLine.Length

    private RoslynServices roslyn = null!;
    private int counter;

    [GlobalSetup]
    public void Setup()
    {
        var config = new Configuration(useTerminalPaletteTheme: true); // avoids theme-file I/O
        roslyn = new RoslynServices(new BenchmarkConsole(), config, new NullTraceLogger());

        // WarmUpAsync awaits initialization and exercises the compile/highlight pipeline so we measure
        // warm steady-state, not first-use JIT/metadata-table construction.
        roslyn.WarmUpAsync(Array.Empty<string>()).GetAwaiter().GetResult();

        for (int i = 0; i < PriorSubmissions; i++)
        {
            var result = roslyn.EvaluateAsync($"var s{i} = {i};").GetAwaiter().GetResult();
            if (result is not EvaluationResult.Success)
                throw new InvalidOperationException($"Setup submission {i} failed: {result}");
        }
    }

    // Every keystroke changes the buffer, so CSharpRepl's highlight cache (keyed on the full text) misses
    // during real typing. A unique trailing comment forces that miss, measuring the true per-keystroke cost.
    [Benchmark(Baseline = true, Description = "Highlight (cache miss = real typing)")]
    public async Task<int> Highlight_CacheMiss()
        => (await roslyn.SyntaxHighlightAsync(Unique(TypedLine))).Count;

    // Identical text every call: a cache hit. This is the re-render path (cursor move, resize, completion-list
    // navigation) — what the cache actually buys. Contrast its time with the cache-miss baseline above.
    [Benchmark(Description = "Highlight (cache hit = re-render)")]
    public async Task<int> Highlight_CacheHit()
        => (await roslyn.SyntaxHighlightAsync(TypedLine)).Count;

    [Benchmark(Description = "Completion list compute")]
    public async Task<int> Complete()
        => (await roslyn.CompleteAsync(UniqueTrailing(TypedLine), Caret)).Count;

    [Benchmark(Description = "ShouldOpenCompletionWindow (every key)")]
    public async Task<bool> ShouldOpenCompletionWindow()
    {
        var key = new KeyPress(new ConsoleKeyInfo('i', ConsoleKey.I, shift: false, alt: false, control: false));
        return await roslyn.ShouldOpenCompletionWindowAsync(TypedLine, Caret, key, CancellationToken.None);
    }

    [Benchmark(Description = "IsCompleteStatement (Enter)")]
    public Task<bool> IsCompleteStatement()
        => roslyn.IsTextCompleteStatementAsync(Unique(TypedLine));

    [Benchmark(Description = "FormatInput (on ; } {)")]
    public async Task<int> FormatInput()
        => (await roslyn.FormatInput(UniqueTrailing(TypedLine), Caret, formatParentNodeOnly: false, CancellationToken.None)).Text.Length;

    // Appends a unique trailing line comment to defeat the text-keyed cache while keeping classification cost
    // and the caret position (which stays within the original line) stable across invocations.
    private string UniqueTrailing(string text) => text + "\n//" + counter++;

    // Same idea for calls where caret doesn't matter.
    private string Unique(string text) => text + "\n//" + counter++;
}
