// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Threading.Tasks;
using CSharpRepl.Services.Roslyn;
using PrettyPrompt.Highlighting;
using PrettyPrompt.Consoles;
using CSharpRepl.Prompt;
using CSharpRepl.Services;
using System.Threading;
using PrettyPrompt;
using System.Linq;

namespace CSharpRepl
{
    static class Program
    {
        private static IConsole console;
        private static RoslynServices roslyn;
        private static IPrompt prompt;

        internal static async Task Main(string[] args)
        {
            console = new SystemConsole();
            Configuration config = ParseArguments(args);
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

            await RunPrompt(config).ConfigureAwait(false);
        }

        private static Configuration ParseArguments(string[] args)
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

        private static async Task RunPrompt(Configuration config)
        {
            // these are required to run before displaying the welcome text.
            // `roslyn` is required for the syntax highlighting in the text,
            // and `prompt` is required because it enables escape sequences.
            roslyn = new RoslynServices(console, config);
            prompt = PromptConfiguration.Create(roslyn);

            console.WriteLine("Welcome to the C# REPL (Read Eval Print Loop)!");
            console.WriteLine("Type C# expressions and statements at the prompt and press Enter to evaluate them.");
            console.WriteLine($"Type {Help} to learn more, and type {Exit} to quit.");
            console.WriteLine(string.Empty);

            await Preload(config).ConfigureAwait(false);

            while (true)
            {
                var response = await prompt.ReadLineAsync("> ").ConfigureAwait(false);
                if (response.IsSuccess)
                {
                    if (response.Text == "exit") { break; }
                    if (response.Text == "help" || response.Text == "?") { PrintHelp(); continue; }

                    var result = await roslyn
                        .Evaluate(response.Text, response.CancellationToken)
                        .ConfigureAwait(false);

                    Print(result, displayDetails: response.IsHardEnter);
                }
            }
        }

        private static async Task Preload(Configuration config)
        {
            bool hasReferences = config.References.Count > 0;
            bool hasLoadScript = config.LoadScript is not null;
            if (!hasReferences && !hasLoadScript)
            {
                _ = roslyn.WarmUpAsync(); // don't await; we don't want to block the console while warmup happens.
                return;
            }

            if(hasReferences)
            {
                console.WriteLine("Adding supplied references...");
                var loadScript = string.Join("\r\n", config.References.Select(reference => $@"#r ""{reference}"""));
                var loadScriptResult = await roslyn.Evaluate(loadScript, CancellationToken.None).ConfigureAwait(false);
                Print(loadScriptResult, displayDetails: false);
            }

            if (hasLoadScript)
            {
                console.WriteLine("Running supplied CSX file...");
                var loadScriptResult = await roslyn.Evaluate(config.LoadScript, CancellationToken.None).ConfigureAwait(false);
                Print(loadScriptResult, displayDetails: false);
            }
        }

        private static void Print(EvaluationResult result, bool displayDetails)
        {
            switch (result)
            {
                case EvaluationResult.Success ok:
                    var formatted = roslyn.PrettyPrint(ok?.ReturnValue, displayDetails);
                    console.WriteLine(formatted);
                    break;
                case EvaluationResult.Error err:
                    var formattedError = roslyn.PrettyPrint(err.Exception, displayDetails);
                    console.WriteErrorLine(AnsiEscapeCodes.Red + formattedError + AnsiEscapeCodes.Reset);
                    break;
                case EvaluationResult.Cancelled:
                    console.WriteErrorLine(
                        AnsiEscapeCodes.Yellow + "Operation cancelled." + AnsiEscapeCodes.Reset
                    );
                    break;
            }
        }

        private static void PrintHelp()
        {
            console.WriteLine(
$@"
Welcome to the C# REPL.
This tool is for rapid experimentation and exploration of code.

Evaluating Code
===============
Type C# at the prompt and press Enter to run it. Its result will be printed.
Shift+Enter will insert a newline instead, to support multiple lines of input.
Ctrl+Enter will evaluate the code, but show detailed member info / stack traces.
If the code isn't complete (e.g. ""{VariableDeclaration}"") pressing Enter will insert a newline.

Adding References
=================
Use the {Reference()} command to add assembly or nuget references.
For assembly references, run {Reference("AssemblyName")} or {Reference("path/to/assembly.dll")}
For nuget packages, run {Reference("nuget: PackageName")} or {Reference("nuget: PackageName, version")}

Use {Preprocessor("#load", "path-to-file")} to evaluate C# stored in files (e.g. csx files). This can
be useful, for example, to build a "".profile.csx"" that includes libraries you want
to load.

Exploring Code
==============
F1, when the caret is in a type or member, will open its MSDN documentation.
Ctrl+F1 will open the type or member's source code on https://source.dot.net/

Configuration Options
=====================
All configuration, including theming, is done at startup via command line flags.
Run --help at the command line to view these options
" 
            );
        }

        private static string Reference(string argument = null) =>
            Preprocessor("#r", argument);

        /// <summary>
        /// Produce syntax-highlighted strings like "#r reference" for the provided <paramref name="argument"/> string.
        /// </summary>
        private static string Preprocessor(string keyword, string argument = null)
        {
            var highlightedKeyword = Color("preprocessor keyword") + keyword + AnsiEscapeCodes.Reset;
            var highlightedArgument = argument is null ? "" : Color("string") + @" """ + argument + @"""" + AnsiEscapeCodes.Reset;

            return highlightedKeyword + highlightedArgument;
        }

        private static string VariableDeclaration =>
            prompt.HasUserOptedOutFromColor
            ? "var x ="
            : (Color("keyword") + "var" + AnsiEscapeCodes.Reset + " " +
               Color("field name") + "x" + AnsiEscapeCodes.Reset + " " +
               Color("operator") + "=" + AnsiEscapeCodes.Reset);

        static string Color(string reference) =>
            AnsiEscapeCodes.ToAnsiEscapeSequence(new ConsoleFormat(roslyn.ToColor(reference)));

        private static string Help =>
            prompt.HasUserOptedOutFromColor
            ? @"""help"""
            : AnsiEscapeCodes.Green + "help" + AnsiEscapeCodes.Reset;

        private static string Exit =>
            prompt.HasUserOptedOutFromColor
            ? @"""exit"""
            : AnsiEscapeCodes.BrightRed + @"exit" + AnsiEscapeCodes.Reset;
    }
}
