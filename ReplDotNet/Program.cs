using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using PrettyPrompt;
using ReplDotNet.Roslyn;
using ReplDotNet.PromptConfiguration;
using PrettyPrompt.Highlighting;
using PrettyPrompt.Completion;
using PrettyPrompt.Consoles;

namespace ReplDotNet
{
    static class Program
    {
        private static RoslynServices roslyn;
        private static PromptAdapter adapter;

        static async Task Main(string[] args)
        {
            roslyn = new RoslynServices();

            Console.WriteLine($"Welcome to {nameof(ReplDotNet)}!");
            Console.WriteLine(@"Evaluate C# expressions at the prompt, and type ""exit"" to stop.");
            Console.WriteLine();
            Console.WriteLine($@"Use the {Preprocessor()} command to add assembly or nuget references.");
            Console.WriteLine($@"For assembly references, run {Preprocessor("AssemblyName")} or {Preprocessor("path/to/assembly.dll")}");
            Console.WriteLine($@"For nuget references, run {Preprocessor("nuget: PackageName")} or {Preprocessor("nuget: PackageName, version")}");
            Console.WriteLine();

            var prompt = ConfigurePrompt();
            adapter = new PromptAdapter();
            roslyn.WarmUp();

            while (true)
            {
                var response = await prompt.ReadLineAsync("> ").ConfigureAwait(false);
                if (response.Success)
                {
                    if (response.Text == "exit") break;

                    var result = await roslyn.Evaluate(new TextInput(response.Text)).ConfigureAwait(false);
                    if (result is EvaluationResult.Error err)
                    {
                        Console.Error.WriteLine(err.Exception.Message);
                    }
                    else if (result is EvaluationResult.Success ok)
                    {
                        Console.WriteLine(ok.ReturnValue);
                    }
                }
            }
        }
        private static string Preprocessor(string reference = null)
        {
            var preprocessor = Color("preprocessor keyword") + "#r" + AnsiEscapeCodes.Reset;
            var argument = reference is null ? "" : Color("string") + @" """ + reference + @"""" + AnsiEscapeCodes.Reset;

            return preprocessor + argument;

            string Color(string reference) =>
                AnsiEscapeCodes.ToAnsiEscapeSequence(new ConsoleFormat(roslyn.ToColor(reference)));
        }

        /// <summary>
        /// Create our callbacks for configuring PrettyPrompt
        /// </summary>
        private static Prompt ConfigurePrompt()
        {
            return new Prompt(completionHandler, highlightHandler, forceSoftEnterHandler);

            static async Task<IReadOnlyList<CompletionItem>> completionHandler(string text, int caret) =>
                adapter.AdaptCompletions(await roslyn.Complete(text, caret).ConfigureAwait(false));

            static async Task<IReadOnlyCollection<FormatSpan>> highlightHandler(string text) =>
                adapter.AdaptSyntaxClassification(await roslyn.ClassifySyntax(text).ConfigureAwait(false));

            static async Task<bool> forceSoftEnterHandler(string text) =>
                !await roslyn.IsTextCompleteStatement(text).ConfigureAwait(false);
        }
    }
}
