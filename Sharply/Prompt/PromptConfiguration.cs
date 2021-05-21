using PrettyPrompt;
using PrettyPrompt.Highlighting;
using Sharply.Services.Roslyn;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace Sharply.Prompt
{
    class PromptConfiguration
    {
        /// <summary>
        /// Create our callbacks for configuring <see cref="PrettyPrompt"/>
        /// </summary>
        public static PrettyPrompt.Prompt Create(RoslynServices roslyn)
        {
            var adapter = new PromptAdapter();
            var appStorage = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), nameof(Sharply));
            var historyStorage = Path.Combine(appStorage, "prompt-history");
            Directory.CreateDirectory(appStorage);

            return new PrettyPrompt.Prompt(historyStorage, new PromptCallbacks
            {
                CompletionCallback = completionHandler,
                HighlightCallback = highlightHandler,
                ForceSoftEnterCallback = forceSoftEnterHandler, 
            });

            async Task<IReadOnlyList<PrettyPrompt.Completion.CompletionItem>> completionHandler(string text, int caret) =>
                adapter.AdaptCompletions(await roslyn.Complete(text, caret).ConfigureAwait(false));

            async Task<IReadOnlyCollection<FormatSpan>> highlightHandler(string text) =>
                adapter.AdaptSyntaxClassification(await roslyn.ClassifySyntax(text).ConfigureAwait(false));

            async Task<bool> forceSoftEnterHandler(string text) =>
                !await roslyn.IsTextCompleteStatement(text).ConfigureAwait(false);
        }
    }
}
