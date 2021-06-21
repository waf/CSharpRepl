using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace CSharpRepl.Tests
{
    public class ProgramTests
    {
        [Fact]
        public async Task MainMethod_Help_ShowsHelp()
        {
            using var outputCollector = OutputCollector.Capture(out var capturedOutput);
            await Program.Main(new[] { "-h" });
            var output = capturedOutput.ToString();

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

            Assert.Contains("C# REPL", output);
            var version = new Version(output[8..]);
            Assert.True(version.Major + version.Minor > 0);
        }
    }

    /// <summary>
    /// Captures standard output. Because there's only one Console.Out,
    /// this forces single threaded execution of unit tests that use it.
    /// </summary>
    public sealed class OutputCollector : IDisposable
    {
        private readonly TextWriter normalStandardOutput;
        private readonly StringWriter fakeConsoleOutput;
        private static readonly Semaphore semaphore = new(1, 1);

        private OutputCollector()
        {
            normalStandardOutput = Console.Out;
            fakeConsoleOutput = new StringWriter();
            Console.SetOut(fakeConsoleOutput);
        }

        public static OutputCollector Capture(out StringWriter capturedOutput)
        {
            semaphore.WaitOne();

            var outputCollector = new OutputCollector();
            capturedOutput = outputCollector.fakeConsoleOutput;
            return outputCollector;
        }

        public void Dispose()
        {
            Console.SetOut(normalStandardOutput);
            semaphore.Release();
        }
    }
}
