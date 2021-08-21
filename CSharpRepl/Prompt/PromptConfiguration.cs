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
    internal static class PromptConfiguration
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
                    [(ConsoleModifiers.Control, ConsoleKey.F1)] = LaunchSourceForSymbol,
                    [ConsoleKey.F11] = DisassembleDebug,
                    [(ConsoleModifiers.Control, ConsoleKey.F11)] = DisassembleRelease,
                }
            });

            async Task<IReadOnlyList<PrettyPrompt.Completion.CompletionItem>> completionHandler(string text, int caret) =>
                adapter.AdaptCompletions(await roslyn.CompleteAsync(text, caret).ConfigureAwait(false));

            async Task<IReadOnlyCollection<FormatSpan>> highlightHandler(string text) =>
                adapter.AdaptSyntaxClassification(await roslyn.SyntaxHighlightAsync(text).ConfigureAwait(false));

            async Task<bool> forceSoftEnterHandler(string text) =>
                !await roslyn.IsTextCompleteStatementAsync(text).ConfigureAwait(false);

            async Task<KeyPressCallbackResult?> LaunchHelpForSymbol(string text, int caret) =>
                LaunchDocumentation(await roslyn.GetSymbolAtIndexAsync(text, caret));

            async Task<KeyPressCallbackResult?> LaunchSourceForSymbol(string text, int caret) =>
                LaunchSource(await roslyn.GetSymbolAtIndexAsync(text, caret));

            Task<KeyPressCallbackResult?> DisassembleDebug(string text, int caret) =>
                Disassemble(roslyn, text, debugMode: true);

            Task<KeyPressCallbackResult?> DisassembleRelease(string text, int caret) =>
                Disassemble(roslyn, text, debugMode: false);
        }

        private static async Task<KeyPressCallbackResult?> Disassemble(RoslynServices roslyn, string text, bool debugMode)
        {
            var ilOutput = await roslyn.ConvertToSyntaxHighlightedIntermediateLanguage(text, debugMode);
            return new KeyPressCallbackResult(text, ilOutput);
        }

        private static KeyPressCallbackResult? LaunchDocumentation(SymbolResult type)
        {
            if (type != SymbolResult.Unknown && type.SymbolDisplay is not null)
            {
                var culture = System.Globalization.CultureInfo.CurrentCulture.Name;
                LaunchBrowser($"https://docs.microsoft.com/{culture}/dotnet/api/{type.SymbolDisplay}");
            }
            return null;
        }

        private static KeyPressCallbackResult? LaunchSource(SymbolResult type)
        {
            if(type != SymbolResult.Unknown && type.SymbolDisplay is not null)
            {
                LaunchBrowser($"https://source.dot.net/#q={type.SymbolDisplay}");
            }

            return null;
        }

        private static KeyPressCallbackResult? LaunchBrowser(string url)
        {
            var opener =
                OperatingSystem.IsWindows() ? "explorer" :
                OperatingSystem.IsMacOS() ? "open" :
                "xdg-open";

            var browser = Process.Start(new ProcessStartInfo(opener, '"' + url + '"')); // wrap in quotes so we can pass through url hashes (#)
            browser?.WaitForExit(); // wait for exit seems to make this work better on WSL2.

            return null;
        }
    }
}
