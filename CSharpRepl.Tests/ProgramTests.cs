using System;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace CSharpRepl.Tests;

public class ProgramTests
{
    private static readonly Regex AnsiEscapeCodeRegex = new(@"\u001b\[.+?m");

    private static string RemoveFormatting(string input) => AnsiEscapeCodeRegex.Replace(input, "");

    [Fact]
    public async Task MainMethod_Help_ShowsHelp()
    {
        using var outputCollector = OutputCollector.Capture(out var capturedOutput);
        await Program.Main(new[] { "-h" });
        var output = capturedOutput.ToString();
        output = RemoveFormatting(output);

        Assert.Contains("Starts a REPL (read eval print loop) according to the provided [OPTIONS].", output);
        // should show default shared framework
        Assert.Contains("Microsoft.NETCore.App (default)", output);
    }

    [Fact]
    public async Task MainMethod_Version_ShowsVersion()
    {
        using var outputCollector = OutputCollector.Capture(out var capturedOutput);

        await Program.Main(new[] { "-v" });

        var output = capturedOutput.ToString();
        output = RemoveFormatting(output);
        Assert.Contains("C# REPL", output);
        var version = new Version(output.Trim("C# REPL-rc-alpha-beta\r\n".ToCharArray()));
        Assert.True(version.Major + version.Minor > 0);
    }

    [Fact]
    public async Task MainMethod_CannotParse_DoesNotThrow()
    {
        using var outputCollector = OutputCollector.Capture(out _, out var capturedError);

        await Program.Main(new[] { "bonk" });

        var error = capturedError.ToString();
        Assert.Equal(
            "Unrecognized command or argument 'bonk'" + Environment.NewLine,
            error
        );
    }
}

/// <summary>
/// Captures standard output. Because there's only one Console.Out,
/// this forces single threaded execution of unit tests that use it.
/// </summary>
public sealed class OutputCollector : IDisposable
{
    private readonly TextWriter normalStandardOutput;
    private readonly TextWriter normalStandardError;
    private readonly StringWriter fakeConsoleOutput;
    private readonly StringWriter fakeConsoleError;
    private static readonly Semaphore semaphore = new(1, 1);

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
