using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace CSharpRepl.Tests;

// These drive Program.RunAsync with an injected fake console (the testing seam), so help/version
// (rendered via Spectre) land in the fake's TestConsole and parse errors land in its captured error
// stream — no process-wide Console.Out/Error swapping required.
public class ProgramTests
{
    [Fact]
    public async Task RunAsync_Help_ShowsHelp()
    {
        var console = FakeConsole.Create();
        console.AnsiConsole.Profile.Width = 1000; // wide so the help text isn't wrapped mid-line

        await Program.RunAsync(["-h"], console);

        var output = console.AnsiConsole.Output.RemoveFormatting();
        Assert.Contains("Starts a REPL (read eval print loop) according to the provided [OPTIONS].", output);
        // should show default shared framework
        Assert.Contains("Microsoft.NETCore.App (default)", output);
    }

    [Fact]
    public async Task RunAsync_Version_ShowsVersion()
    {
        var console = FakeConsole.Create();
        console.AnsiConsole.Profile.Width = 1000;

        await Program.RunAsync(["-v"], console);

        var output = console.AnsiConsole.Output.RemoveFormatting().Split("+")[0]; // remove formatting and trailing git SHA
        Assert.StartsWith("C# REPL", output);
        var version = new Version(output.Trim("C# REPL-rc-alpha-beta\r\n".ToCharArray()));
        Assert.True(version.Major + version.Minor > 0);
    }

    [Fact]
    public async Task RunAsync_CannotParse_DoesNotThrow()
    {
        var (console, _, stderr) = FakeConsole.CreateStubbedOutputAndError();

        var exitCode = await Program.RunAsync(["bonk"], console);

        Assert.Equal(ExitCodes.ErrorParseArguments, exitCode);
        Assert.Equal(
            "Unrecognized command or argument 'bonk'." + Environment.NewLine,
            stderr.ToString()
        );
    }
}

/// <summary>
/// Captures standard output. Because there's only one Console.Out,
/// this forces single threaded execution of unit tests that use it.
/// </summary>
public sealed class OutputCollector : IDisposable
{
    private static readonly Semaphore semaphore = new(1, 1);

    private readonly TextWriter normalStandardOutput;
    private readonly TextWriter normalStandardError;
    private readonly StringWriter fakeConsoleOutput;
    private readonly StringWriter fakeConsoleError;

    private OutputCollector()
    {
        normalStandardOutput = Console.Out;
        normalStandardError = Console.Error;
        fakeConsoleOutput = new StringWriter();
        fakeConsoleError = new StringWriter();
        Console.SetOut(fakeConsoleOutput);
        Console.SetError(fakeConsoleError);
    }

    public static OutputCollector Capture(out StringWriter capturedOutput)
    {
        semaphore.WaitOne();

        var outputCollector = new OutputCollector();
        capturedOutput = outputCollector.fakeConsoleOutput;
        return outputCollector;
    }

    public static OutputCollector Capture(out StringWriter capturedOutput, out StringWriter capturedError)
    {
        semaphore.WaitOne();

        var outputCollector = new OutputCollector();
        capturedOutput = outputCollector.fakeConsoleOutput;
        capturedError = outputCollector.fakeConsoleError;
        return outputCollector;
    }

    public void Dispose()
    {
        Console.SetOut(normalStandardOutput);
        Console.SetOut(normalStandardError);
        semaphore.Release();
    }
}
