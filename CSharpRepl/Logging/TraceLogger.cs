// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using CSharpRepl.Services.Logging;
using System;
using System.IO;
using System.Threading.Tasks;

namespace CSharpRepl.Logging
{
    internal sealed class TraceLogger : ITraceLogger
    {
        private readonly string path;

        private TraceLogger(string path) => this.path = path;

        public void Log(string message) => File.AppendAllText(path, $"{DateTime.UtcNow:s} - {message}{Environment.NewLine}");
        public void Log(Func<string> message) => Log(message());

        public static ITraceLogger Create(string path)
        {
            var tracePath = Path.GetFullPath(path);
            var logger = new TraceLogger(tracePath);

            // let the user know where the trace is being logged to, by writing to the REPL.
            Console.Write(Environment.NewLine + "Writing trace log to ");
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine(tracePath + Environment.NewLine);
            Console.ResetColor();

            AppDomain.CurrentDomain.UnhandledException +=
                (_, evt) => logger.Log("Unhandled Exception: " + evt.ExceptionObject.ToString());
            TaskScheduler.UnobservedTaskException +=
                (_, evt) => logger.Log("Unoberved Task Exception: " + evt.Exception.ToString());

            logger.Log("Trace session starting");

            return logger;
        }
    }
}
