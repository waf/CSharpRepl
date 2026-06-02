// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Threading.Tasks;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Engines;
using CSharpRepl.Services;
using CSharpRepl.Services.Roslyn;
using CSharpRepl.Services.Roslyn.Scripting;

namespace CSharpRepl.Benchmarks;

/// <summary>
/// Profiles the *evaluation* path of a dependent submission chain: each submission references the variable
/// declared by the previous one (var sN = s(N-1) + 1), so every evaluation extends a chain the next must
/// bind through. Measures the wall-clock and allocation to build a <see cref="ChainLength"/>-deep session.
///
/// RunStrategy.Monitoring + invocationCount=1 means each measured run is a single full chain build, preceded
/// by a fresh RoslynServices in [IterationSetup] (evaluation mutates script state, so it can't be reused).
///
/// Run with: dotnet run -c Release --project CSharpRepl.Benchmarks -- --filter *ChainedEvaluation*
/// </summary>
[MemoryDiagnoser]
[SimpleJob(RunStrategy.Monitoring, launchCount: 1, warmupCount: 1, iterationCount: 5, invocationCount: 1)]
public class ChainedEvaluationBenchmark
{
    [Params(10, 50)]
    public int ChainLength;

    private RoslynServices roslyn = null!;

    // Fresh services per measured run; not timed (only the [Benchmark] body is measured).
    [IterationSetup]
    public void IterationSetup()
    {
        var config = new Configuration(useTerminalPaletteTheme: true);
        roslyn = new RoslynServices(new BenchmarkConsole(), config, new NullTraceLogger());
        roslyn.WarmUpAsync(Array.Empty<string>()).GetAwaiter().GetResult();
    }

    [Benchmark]
    public async Task EvaluateChain()
    {
        for (int i = 0; i < ChainLength; i++)
        {
            var code = i == 0 ? "var s0 = 0;" : $"var s{i} = s{i - 1} + 1;";
            var result = await roslyn.EvaluateAsync(code);
            if (result is not EvaluationResult.Success)
                throw new InvalidOperationException($"submission {i} failed: {result}");
        }
    }
}
