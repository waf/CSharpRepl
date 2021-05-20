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
        private static RoslynServices roslyn;

        static async Task Main(string[] args)
        {
            Configuration config = ParseArguments(args);
            if (config is null)
                return;

            if(config.ShowHelpAndExit)
            {
                Console.WriteLine(CommandLine.GetHelp());
                return;
            }

            if(config.ShowVersionAndExit)
            {
                Console.WriteLine(CommandLine.GetVersion());
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
            roslyn = new RoslynServices(config);
            var prompt = PromptConfiguration.Create(roslyn);

            Console.WriteLine($"Welcome to {nameof(Sharply)}!");
            Console.WriteLine(@"Write C# at the prompt and press Enter to evaluate it, and type ""exit"" to stop.");
            Console.WriteLine(@"Press Shift-Enter to insert newlines, and Control-Enter to view detailed member info.");
            Console.WriteLine();
            Console.WriteLine($@"Use the {Preprocessor()} command to add assembly or nuget references.");
            Console.WriteLine($@"For assembly references, run {Preprocessor("AssemblyName")} or {Preprocessor("path/to/assembly.dll")}");
            Console.WriteLine($@"For nuget references, run {Preprocessor("nuget: PackageName")} or {Preprocessor("nuget: PackageName, version")}");
            Console.WriteLine();

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
                Console.WriteLine("Running supplied CSX file...");
                var loadScriptResult = await roslyn.Evaluate(config.LoadScript, CancellationToken.None).ConfigureAwait(false);
                Print(loadScriptResult, displayDetails: false);
            }
            else
            {
                roslyn.WarmUp();
            }
        }

        private static void Print(EvaluationResult result, bool displayDetails)
        {
            switch (result)
            {
                case EvaluationResult.Success ok when ok.ReturnValue is not null:
                    var formatted = roslyn.PrettyPrint(ok.ReturnValue, displayDetails);
                    Console.WriteLine(formatted);
                    break;
                case EvaluationResult.Error err:
                    var formattedError = roslyn.PrettyPrint(err.Exception, displayDetails);
                    Console.Error.WriteLine(AnsiEscapeCodes.Red + formattedError + AnsiEscapeCodes.Reset);
                    break;
                case EvaluationResult.Cancelled:
                    Console.Error.WriteLine(
                        AnsiEscapeCodes.Yellow + "Operation cancelled." + AnsiEscapeCodes.Reset
                    );
                    break;
            }
        }

        /// <summary>
        /// Produce syntax-highlighted strings like "#r reference" for the provided <paramref name="reference"/> string.
        /// </summary>
        private static string Preprocessor(string reference = null)
        {
            var preprocessor = Color("preprocessor keyword") + "#r" + AnsiEscapeCodes.Reset;
            var argument = reference is null ? "" : Color("string") + @" """ + reference + @"""" + AnsiEscapeCodes.Reset;

            return preprocessor + argument;

            static string Color(string reference) =>
                AnsiEscapeCodes.ToAnsiEscapeSequence(new ConsoleFormat(roslyn.ToColor(reference)));
        }
    }
}
