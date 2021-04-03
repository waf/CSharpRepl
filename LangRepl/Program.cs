using PrettyPrompt;
using PrettyPrompt.Highlighting;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace LangRepl
{
    class Program
    {
        private static ReplServices repl;

        static async Task Main(string[] args)
        {
            var prompt = new Prompt(completionHandler: complete, highlightHandler: highlight, forceSoftEnterHandler: ShouldBeSoftEnter);
            repl = new ReplServices();
            repl.WarmUp();

            while (true)
            {
                var response = await prompt.ReadLineAsync("> ").ConfigureAwait(false);
                if (response.Success)
                {
                    if (response.Text == "exit") break;

                    var result = await repl.Evaluate(new TextInput(response.Text)).ConfigureAwait(false);
                    if(result is EvaluationResult.Error err)
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

        private static async Task<bool> ShouldBeSoftEnter(string text)
        {
            bool isComplete = await repl.IsTextCompleteStatement(text).ConfigureAwait(false);
            return !isComplete;
        }

        private static async Task<IReadOnlyCollection<FormatSpan>> highlight(string text)
        {
            var results = await repl.Highlight(text).ConfigureAwait(false);

            return results
                .Select(r => new FormatSpan(r.TextSpan.Start, r.TextSpan.Length, ToColor(r.ClassificationType)))
                .Where(f => f.Formatting is not null)
                .ToArray();
        }

        private static ConsoleFormat ToColor(string classificationType) =>
            classificationType switch
            {
                "string" => new ConsoleFormat(AnsiColor.BrightYellow),
                "number" => new ConsoleFormat(AnsiColor.BrightBlue),
                "operator" => new ConsoleFormat(AnsiColor.Magenta),
                "keyword" => new ConsoleFormat(AnsiColor.Magenta),
                "keyword - control" => new ConsoleFormat(AnsiColor.Magenta),

                "record class name" => new ConsoleFormat(AnsiColor.BrightCyan),
                "class name" => new ConsoleFormat(AnsiColor.BrightCyan),
                "struct name" => new ConsoleFormat(AnsiColor.BrightCyan),

                "comment" => new ConsoleFormat(AnsiColor.Cyan),
                _ => null
            };

        private static async Task<IReadOnlyList<PrettyPrompt.Completion.CompletionItem>> complete(string text, int caret)
        {
            var results = await repl.Complete(text, caret).ConfigureAwait(false);
            return results?.Items
                .OrderByDescending(i => i.Rules.MatchPriority)
                .Select(r => new PrettyPrompt.Completion.CompletionItem
                {
                    StartIndex = r.Span.Start,
                    ReplacementText = r.DisplayText
                })
                .ToArray()
                ??
                Array.Empty<PrettyPrompt.Completion.CompletionItem>();
        }
    }
}
