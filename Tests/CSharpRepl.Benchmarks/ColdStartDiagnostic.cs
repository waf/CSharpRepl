// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using CSharpRepl.Services;
using CSharpRepl.Services.Logging;
using CSharpRepl.Services.Roslyn;
using PrettyPrompt.Consoles;

namespace CSharpRepl.Benchmarks;

/// <summary>Trace logger that prints each warm-up milestone with the elapsed time since construction.</summary>
internal sealed class TimestampingTraceLogger : ITraceLogger
{
    private readonly Stopwatch sw = Stopwatch.StartNew();
    public void Log(string message) => Console.WriteLine($"    [{sw.ElapsedMilliseconds,6}ms] {message}");
    public void Log(Func<string> message) { }
    public void LogPaths(string message, Func<IEnumerable<string?>> paths) { }
}

/// <summary>
/// NOT a BenchmarkDotNet benchmark. BenchmarkDotNet measures warm steady-state by design (it JIT-warms before
/// measuring), so it is blind to the thing we care about here: the cost the user pays on the *very first*
/// keystroke of a session, which is dominated by first-run JIT + first-compilation of the editor pipeline.
///
/// Each invocation must be a fresh process to observe true cold cost — once a code path JITs, it stays JIT'd.
/// Run one mode per process:
///   dotnet run -c Release --project CSharpRepl.Benchmarks -- coldstart raw      # init done, pipeline cold (worst case)
///   dotnet run -c Release --project CSharpRepl.Benchmarks -- coldstart warmed   # full WarmUpAsync awaited first (best case)
///   dotnet run -c Release --project CSharpRepl.Benchmarks -- coldstart race     # warmup fired-and-forgotten, then type after a short delay (real app)
/// </summary>
internal static class ColdStartDiagnostic
{
    public static async Task RunAsync(string mode)
    {
        var sw = Stopwatch.StartNew();
        var config = new Configuration(useTerminalPaletteTheme: true); // avoids theme-file I/O

        // "warmtiming": just print when each warm-up milestone is reached (editor path vs. full warmup).
        if (mode == "warmtiming")
        {
            var r = new RoslynServices(new BenchmarkConsole(), config, new TimestampingTraceLogger());
            var ws = Stopwatch.StartNew();
            await r.WarmUpAsync(Array.Empty<string>());
            Console.WriteLine($"[{mode}] WarmUpAsync fully completed in {ws.ElapsedMilliseconds}ms");
            return;
        }

        var roslyn = new RoslynServices(new BenchmarkConsole(), config, new NullTraceLogger());
        Console.WriteLine($"[{mode}] RoslynServices ctor returned at {sw.ElapsedMilliseconds}ms");

        switch (mode)
        {
            case "warmed":
            {
                var ws = Stopwatch.StartNew();
                await roslyn.WarmUpAsync(Array.Empty<string>());
                Console.WriteLine($"[{mode}] WarmUpAsync completed in {ws.ElapsedMilliseconds}ms");
                break;
            }
            case "race":
            {
                // Mirror Preload: fire-and-forget, then the user reads the banner and reaches for the keyboard.
                _ = roslyn.WarmUpAsync(Array.Empty<string>());
                var delay = int.Parse(Environment.GetEnvironmentVariable("RACE_DELAY_MS") ?? "200");
                await Task.Delay(delay);
                Console.WriteLine($"[{mode}] typed after {delay}ms (warmup still running in background)");
                break;
            }
            default: // "raw": let background init finish, but leave the keystroke pipeline un-JIT'd.
            {
                // CurrentDocumentForProfiling is null until Initialization completes; polling it does not
                // touch the highlight/completion code paths, so the pipeline stays cold.
                while (roslyn.CurrentDocumentForProfiling is null)
                    await Task.Delay(5);
                Console.WriteLine($"[{mode}] background init complete at {sw.ElapsedMilliseconds}ms");
                break;
            }
        }

        // The very first keystroke. Typing 's' as the first character drives exactly these PrettyPrompt
        // callbacks, in this order, serially (see CSharpReplPromptCallbacks + Prompt.ReadLineAsync):
        //   1. HighlightCallbackAsync          -> roslyn.SyntaxHighlightAsync
        //   2. ShouldOpenCompletionWindowAsync -> roslyn.ShouldOpenCompletionWindowAsync
        //   3. (window opens) GetSpanToReplaceByCompletionAsync
        //   4. (window opens) GetCompletionItemsAsync -> roslyn.CompleteAsync
        const string text = "s";
        const int caret = 1;
        var key = new KeyPress(new ConsoleKeyInfo('s', ConsoleKey.S, shift: false, alt: false, control: false));

        Console.WriteLine($"[{mode}] --- first keystroke ('{text}') ---");
        var total = Stopwatch.StartNew();

        var t = Stopwatch.StartNew();
        var spans = await roslyn.SyntaxHighlightAsync(text);
        Console.WriteLine($"  1. Highlight                  {t.Elapsed.TotalMilliseconds,8:F1} ms   ({spans.Count} spans)");

        t.Restart();
        var shouldOpen = await roslyn.ShouldOpenCompletionWindowAsync(text, caret, key, default);
        Console.WriteLine($"  2. ShouldOpenCompletionWindow {t.Elapsed.TotalMilliseconds,8:F1} ms   (= {shouldOpen})");

        // Steps 3 & 4 only happen when the window actually opens — exactly as PrettyPrompt drives them.
        // While completion is suppressed during warm-up, ShouldOpen returns false and the app makes neither call.
        if (shouldOpen)
        {
            t.Restart();
            _ = await roslyn.GetSpanToReplaceByCompletionAsync(text, caret, default);
            Console.WriteLine($"  3. GetSpanToReplace           {t.Elapsed.TotalMilliseconds,8:F1} ms");

            t.Restart();
            var completions = await roslyn.CompleteAsync(text, caret, default);
            Console.WriteLine($"  4. Complete                   {t.Elapsed.TotalMilliseconds,8:F1} ms   ({completions.Count} items)");
        }
        else
        {
            Console.WriteLine($"  3. GetSpanToReplace                  -      (window suppressed)");
            Console.WriteLine($"  4. Complete                          -      (window suppressed)");
        }

        Console.WriteLine($"  ============================================");
        Console.WriteLine($"  first-keystroke total         {total.Elapsed.TotalMilliseconds,8:F1} ms");
    }
}
