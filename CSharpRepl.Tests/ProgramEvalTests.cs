// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.IO;
using System.Threading.Tasks;
using CSharpRepl.Services.Roslyn;
using Xunit;

namespace CSharpRepl.Tests;

[Collection(nameof(RoslynServices))]
public class ProgramEvalTests
{
    [Fact]
    public async Task MainMethod_Eval_ExplicitWriteLine_PrintsToStdout()
    {
        using var outputCollector = OutputCollector.Capture(out var stdout);

        var exitCode = await Program.Main(["-e", "Console.WriteLine(40 + 2)"]);

        Assert.Equal(ExitCodes.Success, exitCode);
        Assert.Contains("42", stdout.ToString());
    }

    [Fact]
    public async Task MainMethod_Eval_BareExpression_EvaluatesAndExitsSuccessfully()
    {
        using var outputCollector = OutputCollector.Capture(out var stdout);

        var exitCode = await Program.Main(["-e", "1 + 1"]);

        Assert.Equal(ExitCodes.Success, exitCode);
        // Auto-print emits the value followed by a single trailing newline (standard CLI output) — no
        // decoration or color. A scalar renders as just its text, so the whole stdout is "2" + newline.
        Assert.Equal("2" + Environment.NewLine, stdout.ToString());
    }

    [Fact]
    public async Task MainMethod_EvalFile_RunsFileAndExits()
    {
        // The file deliberately contains a ' ' char literal — the case that can't be cleanly passed
        // inline through a shell. --eval-file reads & runs the file (unlike a positional .csx, which
        // would drop into the interactive REPL).
        var path = Path.Combine(Path.GetTempPath(), $"csr_eval_{Guid.NewGuid():N}.csx");
        await File.WriteAllTextAsync(path, "System.Console.WriteLine(\"a b c\".Split(' ').Length);", TestContext.Current.CancellationToken);
        try
        {
            using var outputCollector = OutputCollector.Capture(out var stdout);

            var exitCode = await Program.Main(["--eval-file", path]);

            Assert.Equal(ExitCodes.Success, exitCode);
            Assert.Contains("3", stdout.ToString());
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task MainMethod_Eval_RuntimeException_WritesToStderrAndReturnsErrorCode()
    {
        using var outputCollector = OutputCollector.Capture(out _, out var stderr);

        var exitCode = await Program.Main(["-e", "throw new System.Exception(\"boom\");"]);

        Assert.NotEqual(ExitCodes.Success, exitCode);
        Assert.Contains("boom", stderr.ToString());
    }

    [Fact]
    public async Task MainMethod_EvalAndEvalFile_BothSpecified_ReturnsParseError()
    {
        using var outputCollector = OutputCollector.Capture(out _, out var stderr);

        var exitCode = await Program.Main(["-e", "1", "--eval-file", "does-not-matter.csx"]);

        Assert.Equal(ExitCodes.ErrorParseArguments, exitCode);
        Assert.Contains("only one of --eval", stderr.ToString());
    }

    [Fact]
    public async Task MainMethod_Eval_WithReference_AppliesReferenceAndSucceeds()
    {
        // Exercises the preload path that applies command-line --reference values before evaluation.
        using var outputCollector = OutputCollector.Capture(out _);

        var exitCode = await Program.Main(["-e", "Enumerable.Range(1, 3).Count()", "-r", "System.Linq"]);

        Assert.Equal(ExitCodes.Success, exitCode);
    }
}
