// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Threading.Tasks;
using PrettyPrompt.Consoles;
using CSharpRepl.Services;
using CSharpRepl.Services.Roslyn;
using CSharpRepl.PrettyPromptConfig;

namespace CSharpRepl
{
    /// <summary>
    /// Main entry point; parses command line args and starts the <see cref="ReadEvalPrintLoop"/>.
    /// Check out ARCHITECTURE.md in the root of the repo for some design documentation.
    /// </summary>
    static class Program
    {
        internal static async Task Main(string[] args)
        {
            var console = new SystemConsole();
            Configuration? config = ParseArguments(args);
            if (config is null)
                return;

            if(config.ShowHelpAndExit)
            {
                console.WriteLine(CommandLine.GetHelp());
                return;
            }

            if(config.ShowVersionAndExit)
            {
                console.WriteLine(CommandLine.GetVersion());
                return;
            }

            var roslyn = new RoslynServices(console, config);
            var prompt = PromptConfiguration.Create(console, roslyn);

            await new ReadEvalPrintLoop(roslyn, prompt, console)
                .RunAsync(config)
                .ConfigureAwait(false);
        }

        private static Configuration? ParseArguments(string[] args)
        {
            try
            {
                return CommandLine.ParseArguments(args);
            }
            catch (Exception ex)
            {
                Console.WriteLine(CommandLine.GetHelp());
                Console.ForegroundColor = ConsoleColor.Red;
                Console.Error.WriteLine(ex.Message);
                Console.ResetColor();
                Console.WriteLine();
                return null;
            }
        }
    }
}
