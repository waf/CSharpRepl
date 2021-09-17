using CSharpRepl.Logging;
using CSharpRepl.Services.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using Xunit;

namespace CSharpRepl.Tests
{
    public class TraceLoggerTests
    {
        [Fact]
        public void Test()
        {
            // more of an integration test than anything; we want to
            // make sure the logger actually does log to a file.

            var path = Path.GetTempFileName();
            var logger = TraceLogger.Create(path);
            logger.Log("Hello World, I'm a hopeful and optimistic REPL.");
            logger.Log("Arrgghh an error");

            var loggedLines = File.ReadAllLines(path);

            Assert.Contains("Trace session starting", loggedLines[0]);
            Assert.Contains("Hello World, I'm a hopeful and optimistic REPL.", loggedLines[1]);
            Assert.Contains("Arrgghh an error", loggedLines[2]);
        }
    }

    /// <summary>
    /// Executes the delayed evaluation Funcs for testing purposes (to make sure they don't throw).
    /// </summary>
    public class TestTraceLogger : ITraceLogger
    {
        public void Log(string message) { }

        public void Log(Func<string> message) { message(); }

        public void LogPaths(string message, Func<IEnumerable<string>> paths) => paths();
    }
}
