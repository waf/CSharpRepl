using System;
using System.Threading.Tasks;
using Sharply.Services.Roslyn;
using PrettyPrompt.Highlighting;
using PrettyPrompt.Consoles;
using Sharply.Prompt;
using Sharply.Services;
using System.Threading;

namespace Sharply
{
    static class Program
    {
        private static IConsole console;
        private static RoslynServices roslyn;

        static async Task Main(string[] args)
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
            var prompt = PromptConfiguration.Create(roslyn);

            console.WriteLine($"Welcome to the C# REPL (Read Eval Print Loop)!");
            console.WriteLine(@"Write C# at the prompt and press Enter to evaluate it, and type ""exit"" to stop.");
            console.WriteLine(@"Press Shift-Enter to insert newlines, and Control-Enter to view detailed member info.");
            console.WriteLine(string.Empty);
            console.WriteLine($@"Use the {Reference()} command to add assembly or nuget references.");
            console.WriteLine($@"For assembly references, run {Reference("AssemblyName")} or {Reference("path/to/assembly.dll")}");
            console.WriteLine($@"For nuget references, run {Reference("nuget: PackageName")} or {Reference("nuget: PackageName, version")}");
            console.WriteLine(string.Empty);

            await Preload(config).ConfigureAwait(false);

            while (true)
            {
                var response = await prompt.ReadLineAsync("> ").ConfigureAwait(false);
                if (response.IsSuccess)
                {
                    if (response.Text == "exit") break;

                    var result = await roslyn
                        .Evaluate(response.Text, response.CancellationToken)
                        .ConfigureAwait(false);

                    Print(result, displayDetails: response.IsHardEnter);
                }
            }
        }

        private static async Task Preload(Configuration config)
        {
            if (config.LoadScript is not null)
            {
                console.WriteLine("Running supplied CSX file...");
                var loadScriptResult = await roslyn.Evaluate(config.LoadScript, CancellationToken.None).ConfigureAwait(false);
                Print(loadScriptResult, displayDetails: false);
            }
            else
            {
                _ = roslyn.WarmUpAsync(); //purposely don't await, we don't want to block the console while warmup happens.
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

        /// <summary>
        /// Produce syntax-highlighted strings like "#r reference" for the provided <paramref name="reference"/> string.
        /// </summary>
        private static string Reference(string reference = null)
        {
            var preprocessor = Color("preprocessor keyword") + "#r" + AnsiEscapeCodes.Reset;
            var argument = reference is null ? "" : Color("string") + @" """ + reference + @"""" + AnsiEscapeCodes.Reset;

            return preprocessor + argument;

            static string Color(string reference) =>
                AnsiEscapeCodes.ToAnsiEscapeSequence(new ConsoleFormat(roslyn.ToColor(reference)));
        }
    }
}
