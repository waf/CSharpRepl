// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Linq;
using System.Threading.Tasks;
using BenchmarkDotNet.Attributes;
using CSharpRepl.Services;
using CSharpRepl.Services.Roslyn;
using CSharpRepl.Services.Roslyn.Scripting;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Classification;
using Microsoft.CodeAnalysis.Text;

namespace CSharpRepl.Benchmarks;

/// <summary>
/// Attributes the per-keystroke highlight cost (and its allocation) to a stage of the Roslyn pipeline,
/// to find where the ~36 MB/keystroke at depth 50 comes from. The session is a *dependent* chain — each
/// prior submission references the previous one (var sN = s(N-1) + 1) — and the "typed" line references
/// the most-recent variable, so binding must resolve through the whole submission chain.
///
/// Compare the Allocated column across stages at PriorSubmissions=50:
///   Fork           - just CurrentDocument.WithText (no analysis)
///   GetSyntaxRoot  - parse the current submission (expected depth-independent)
///   GetCompilation - build the project's Compilation (walks the submission-project chain)
///   GetSemanticModel - compilation + bind the current document
///   Classify       - what production highlighting actually calls each keystroke
///
/// Run with: dotnet run -c Release --project CSharpRepl.Benchmarks -- --filter *AllocationBreakdown*
/// </summary>
[MemoryDiagnoser]
[SimpleJob(launchCount: 1, warmupCount: 3, iterationCount: 5)]
public class AllocationBreakdownBenchmark
{
    [Params(0, 50)]
    public int PriorSubmissions;

    private RoslynServices roslyn = null!;
    private Document baseDocument = null!;
    private string typedLine = "";
    private int counter;

    [GlobalSetup]
    public void Setup()
    {
        var config = new Configuration(useTerminalPaletteTheme: true);
        roslyn = new RoslynServices(new BenchmarkConsole(), config, new NullTraceLogger());
        roslyn.WarmUpAsync(Array.Empty<string>()).GetAwaiter().GetResult();

        for (int i = 0; i < PriorSubmissions; i++)
        {
            var code = i == 0 ? "var s0 = 0;" : $"var s{i} = s{i - 1} + 1;";
            var result = roslyn.EvaluateAsync(code).GetAwaiter().GetResult();
            if (result is not EvaluationResult.Success)
                throw new InvalidOperationException($"Setup submission {i} failed: {result}");
        }

        baseDocument = roslyn.CurrentDocumentForProfiling!;
        // Reference the most-recent variable, forcing binding through the chain (depth-0 case uses a framework call).
        typedLine = PriorSubmissions == 0 ? "System.Console.WriteLine(1)" : $"s{PriorSubmissions - 1}.ToString()";
    }

    // A fresh fork each invocation (unique trailing comment) so the per-keystroke "buffer changed" path is
    // measured, not a cached re-render.
    private Document Current() => baseDocument.WithText(SourceText.From(typedLine + "\n//" + counter++));

    [Benchmark(Description = "1. WithText (fork only)")]
    public Document Fork() => Current();

    [Benchmark(Description = "2. GetSyntaxRoot (parse)")]
    public async Task<SyntaxNode?> GetSyntaxRoot() => await Current().GetSyntaxRootAsync();

    [Benchmark(Description = "3. GetCompilation (build chain)")]
    public async Task<Compilation?> GetCompilation() => await Current().Project.GetCompilationAsync();

    [Benchmark(Description = "4. GetSemanticModel (bind)")]
    public async Task<SemanticModel?> GetSemanticModel() => await Current().GetSemanticModelAsync();

    [Benchmark(Baseline = true, Description = "5. GetClassifiedSpans (full = production)")]
    public async Task<int> Classify()
    {
        var doc = Current();
        var length = (await doc.GetTextAsync()).Length;
        var spans = await Classifier.GetClassifiedSpansAsync(doc, TextSpan.FromBounds(0, length));
        return spans.Count();
    }
}
