// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using PrettyPrompt;
using PrettyPrompt.Highlighting;
using CSharpRepl.Services.Roslyn;
using CSharpRepl.Services.SymbolExploration;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;

namespace CSharpRepl.Prompt
{
    static class PromptConfiguration
    {
        /// <summary>
        /// Create our callbacks for configuring <see cref="PrettyPrompt"/>
        /// </summary>
        public static PrettyPrompt.Prompt Create(RoslynServices roslyn)
        {
            var adapter = new PromptAdapter();
            var appStorage = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), ".csharprepl");
            var historyStorage = Path.Combine(appStorage, "prompt-history");
            Directory.CreateDirectory(appStorage);

            return new PrettyPrompt.Prompt(historyStorage, new PromptCallbacks
            {
                CompletionCallback = completionHandler,
                HighlightCallback = highlightHandler,
                ForceSoftEnterCallback = forceSoftEnterHandler, 
                KeyPressCallbacks =
                {
                    [ConsoleKey.F1] = LaunchHelpForSymbol,
                    [(ConsoleModifiers.Control, ConsoleKey.F1)] = LaunchSourceForSymbol
                }
            });

            async Task<IReadOnlyList<PrettyPrompt.Completion.CompletionItem>> completionHandler(string text, int caret) =>
                adapter.AdaptCompletions(await roslyn.Complete(text, caret).ConfigureAwait(false));

            async Task<IReadOnlyCollection<FormatSpan>> highlightHandler(string text) =>
                adapter.AdaptSyntaxClassification(await roslyn.SyntaxHighlightAsync(text).ConfigureAwait(false));

            async Task<bool> forceSoftEnterHandler(string text) =>
                !await roslyn.IsTextCompleteStatement(text).ConfigureAwait(false);

            async Task LaunchHelpForSymbol(string text, int caret) =>
                LaunchDocumentation(await roslyn.GetSymbolAtIndex(text, caret));

            async Task LaunchSourceForSymbol(string text, int caret) =>
                LaunchSource(await roslyn.GetSymbolAtIndex(text, caret));
        }

        private static void LaunchDocumentation(SymbolResult type)
        {
            if(type != SymbolResult.Unknown && type.SymbolDisplay is not null)
            {
                var culture = System.Globalization.CultureInfo.CurrentCulture.Name;
                LaunchBrowser($"https://docs.microsoft.com/{culture}/dotnet/api/{type.SymbolDisplay}");
            }
        }

        private static void LaunchSource(SymbolResult type)
        {
            if(type != SymbolResult.Unknown && type.SymbolDisplay is not null)
            {
                LaunchBrowser($"https://source.dot.net/#q={type.SymbolDisplay}");
            }
        }

        private static void LaunchBrowser(string url)
        {
            var opener =
                OperatingSystem.IsWindows() ? "explorer" :
                OperatingSystem.IsMacOS() ? "open" :
                "xdg-open";

            Process.Start(new ProcessStartInfo(opener, '"' + url + '"')); // wrap in quotes so we can pass through url hashes (#)
        }
    }
}
