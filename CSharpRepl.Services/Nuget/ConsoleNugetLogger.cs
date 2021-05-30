// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Threading.Tasks;
using NuGet.Common;
using PrettyPrompt.Consoles;

namespace Sharply.Services.Nuget
{
    public class ConsoleNugetLogger : ILogger
    {
        private readonly IConsole console;

        public ConsoleNugetLogger(IConsole console)
        {
            this.console = console;
        }

        public void Log(ILogMessage message) =>
            Log(message.Level, message.Message);

        public void Log(LogLevel level, string data)
        {
            switch (level)
            {
                case LogLevel.Debug: LogDebug(data); return;
                case LogLevel.Verbose: LogVerbose(data); return;
                case LogLevel.Information: LogInformation(data); return;
                case LogLevel.Minimal: LogMinimal(data); return;
                case LogLevel.Warning: LogWarning(data); return;
                case LogLevel.Error: LogError(data); return;
                default: return;
            }
        }

        public void LogMinimal(string data) =>
            console.WriteLine(Truncate(data));

        public void LogWarning(string data) =>
            console.WriteLine(Truncate(data));

        public void LogError(string data) =>
            console.WriteErrorLine(data);

        public void LogInformationSummary(string data) =>
            console.WriteLine(Truncate(data));

        /// <summary>
        /// Nuget output can be a bit overwhelming. Truncate some of the longer lines
        /// </summary>
        private static string Truncate(string data)
        {
            if (string.IsNullOrWhiteSpace(data)) return "";

            if (data.IndexOf(" to folder") is int discard1 and >= 0)
                return data.Substring(0, discard1);

            if (data.IndexOf(" with respect to project") is int discard2 and >= 0)
                return data.Substring(0, discard2);

            if (data.StartsWith("Successfully installed") && data.IndexOf(" to ") is int discard3 and >= 0)
                return data.Substring(0, discard3);

            return data;
        }

        // unused
        public Task LogAsync(LogLevel level, string data) => Task.CompletedTask;
        public Task LogAsync(ILogMessage message) => Task.CompletedTask;
        public void LogDebug(string data) { /* ignore, we don't need this much output */ } 
        public void LogVerbose(string data) { /* ignore, we don't need this much output */ }
        public void LogInformation(string data) { /* ignore, we don't need this much output */ }
    }
}
