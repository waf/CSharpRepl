using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using PrettyPrompt;
using Sharply.Services.Roslyn;
using Sharply.PromptConfiguration;
using PrettyPrompt.Highlighting;
using PrettyPrompt.Completion;
using PrettyPrompt.Consoles;
using System.IO;

namespace Sharply
{
    class Program
    {
        private static RoslynServices roslyn;
        private static PromptAdapter adapter;

        static async Task Main(string[] args)
        {
            // these are required to run before displaying the welcome text.
            // `roslyn` is required for the syntax highlighting in the text,
            // and `prompt` is required because it enables escape sequences.
            roslyn = new RoslynServices();
            var prompt = ConfigurePrompt();

            Console.WriteLine($"Welcome to {nameof(Sharply)}!");
            Console.WriteLine(@"Write C# at the prompt and press Enter to evaluate it, and type ""exit"" to stop.");
            Console.WriteLine(@"Press Shift-Enter to insert newlines, and Control-Enter to view detailed member info.");
            Console.WriteLine();
            Console.WriteLine($@"Use the {Preprocessor()} command to add assembly or nuget references.");
            Console.WriteLine($@"For assembly references, run {Preprocessor("AssemblyName")} or {Preprocessor("path/to/assembly.dll")}");
            Console.WriteLine($@"For nuget references, run {Preprocessor("nuget: PackageName")} or {Preprocessor("nuget: PackageName, version")}");
            Console.WriteLine();

            adapter = new PromptAdapter();
            roslyn.WarmUp();

            while (true)
            {
                var response = await prompt.ReadLineAsync("> ").ConfigureAwait(false);
                if (response.IsSuccess)
                {
                    if (response.Text == "exit") break;

                    var result = await roslyn
                        .Evaluate(response.Text, response.CancellationToken)
                        .ConfigureAwait(false);

                    switch (result)
                    {
                        case EvaluationResult.Success ok when ok.ReturnValue is not null:
                            var formatted = roslyn.PrettyPrint(ok.ReturnValue, displayDetails: response.IsHardEnter);
                            Console.WriteLine(formatted);
                            break;
                        case EvaluationResult.Error err:
                            Console.Error.WriteLine(
                                AnsiEscapeCodes.Red + err.Exception.Message + AnsiEscapeCodes.Reset
                            );
                            break;
                        case EvaluationResult.Cancelled:
                            Console.Error.WriteLine(
                                AnsiEscapeCodes.Yellow + "Operation cancelled." + AnsiEscapeCodes.Reset
                            );
                            break;
                    }
                }
            }
        }

        private static void DoSomething(string[] arg)
        {
            ;
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

        /// <summary>
        /// Create our callbacks for configuring <see cref="PrettyPrompt"/>
        /// </summary>
        private static PrettyPrompt.Prompt ConfigurePrompt()
        {
            var appStorage = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), nameof(Sharply));
            var historyStorage = Path.Combine(appStorage, "prompt-history");
            Directory.CreateDirectory(appStorage);

            return new PrettyPrompt.Prompt(historyStorage, new PromptCallbacks
            {
                CompletionCallback = completionHandler,
                HighlightCallback = highlightHandler,
                ForceSoftEnterCallback = forceSoftEnterHandler, 
            });

            static async Task<IReadOnlyList<CompletionItem>> completionHandler(string text, int caret) =>
                adapter.AdaptCompletions(await roslyn.Complete(text, caret).ConfigureAwait(false));

            static async Task<IReadOnlyCollection<FormatSpan>> highlightHandler(string text) =>
                adapter.AdaptSyntaxClassification(await roslyn.ClassifySyntax(text).ConfigureAwait(false));

            static async Task<bool> forceSoftEnterHandler(string text) =>
                !await roslyn.IsTextCompleteStatement(text).ConfigureAwait(false);
        }
    }
}
