// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Threading.Tasks;
using PrettyPrompt.Consoles;
using CSharpRepl.Services;
using CSharpRepl.Services.Roslyn;
using CSharpRepl.PrettyPromptConfig;
using System.IO;
using PrettyPrompt;
using CSharpRepl.Logging;
using CSharpRepl.Services.Logging;
using System.Diagnostics.CodeAnalysis;

namespace CSharpRepl
{
    /// <summary>
    /// Main entry point; parses command line args and starts the <see cref="ReadEvalPrintLoop"/>.
    /// Check out ARCHITECTURE.md in the root of the repo for some design documentation.
    /// </summary>
    static class Program
    {
        internal static async Task<int> Main(string[] args)
        {
            var console = new SystemConsole();

            if (!TryParseArguments(args, out var config))
                return 1;

            if (config.OutputForEarlyExit is not null)
            {
                console.WriteLine(config.OutputForEarlyExit);
                return 0;
            }

            var appStorage = CreateApplicationStorageDirectory();

            var logger = InitializeLogging(config.Trace);
            var roslyn = new RoslynServices(console, config, logger);
            var prompt = new Prompt(
                persistentHistoryFilepath: Path.Combine(appStorage, "prompt-history"),
                callbacks: PromptConfiguration.Configure(console, roslyn)
            );

            await new ReadEvalPrintLoop(roslyn, prompt, console)
                .RunAsync(config)
                .ConfigureAwait(false);

            return 0;
        }

        private static bool TryParseArguments(string[] args, [NotNullWhen(true)] out Configuration? configuration)
        {
            try
            {
                configuration = CommandLine.Parse(args);
                return true;
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.Error.WriteLine(ex.Message);
                Console.ResetColor();
                Console.WriteLine();
                configuration = null;
                return false;
            }
        }

        /// <summary>
        /// Create application storage directory and return its path.
        /// This is where prompt history and nuget packages are stored.
        /// </summary>
        private static string CreateApplicationStorageDirectory()
        {
            var appStorage = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), ".csharprepl");
            Directory.CreateDirectory(appStorage);
            return appStorage;
        }

        /// <summary>
        /// Initialize logging. It's off by default, unless the user passes the --trace flag.
        /// </summary>
        private static ITraceLogger InitializeLogging(bool trace)
        {
            if (!trace)
            {
                return new NullLogger();
            }

            return TraceLogger.Create($"csharprepl-tracelog-{DateTime.UtcNow:yyyy-MM-dd}.txt");
        }
    }
}
