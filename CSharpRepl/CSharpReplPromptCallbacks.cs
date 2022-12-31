// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CSharpRepl.Services;
using CSharpRepl.Services.Roslyn;
using CSharpRepl.Services.Roslyn.Scripting;
using CSharpRepl.Services.SymbolExploration;
using CSharpRepl.Services.SyntaxHighlighting;
using Microsoft.CodeAnalysis;
using PrettyPrompt;
using PrettyPrompt.Completion;
using PrettyPrompt.Consoles;
using PrettyPrompt.Documents;
using PrettyPrompt.Highlighting;
using RoslynCharacterSetModificationRule = Microsoft.CodeAnalysis.Completion.CharacterSetModificationRule;

namespace CSharpRepl.PrettyPromptConfig;

/// <summary>
/// An implementation of <see cref="PrettyPrompt.PromptCallbacks"/> that configures C#-specific
/// behavior for our prompt using Roslyn.
/// </summary>
internal class CSharpReplPromptCallbacks : PromptCallbacks
{
    private readonly IConsoleEx console;
    private readonly RoslynServices roslyn;
    private readonly Configuration configuration;

    public CSharpReplPromptCallbacks(IConsoleEx console, RoslynServices roslyn, Configuration configuration)
    {
        this.console = console;
        this.roslyn = roslyn;
        this.configuration = configuration;
    }

    protected override IEnumerable<(KeyPressPattern Pattern, KeyPressCallbackAsync Callback)> GetKeyPressCallbacks()
    {
        yield return (
            new(ConsoleKey.F1),
            async (text, caret, cancellationToken) => LaunchDocumentation(await roslyn.GetSymbolAtIndexAsync(text, caret)));

        yield return (
            new(ConsoleModifiers.Control, ConsoleKey.F1),
            async (text, caret, cancellationToken) => LaunchSource(await roslyn.GetSymbolAtIndexAsync(text, caret)));

        yield return (
            new(ConsoleKey.F9),
            (text, caret, cancellationToken) => Disassemble(roslyn, text, console, debugMode: true));

        yield return (
            new(ConsoleModifiers.Control, ConsoleKey.F9),
            (text, caret, cancellationToken) => Disassemble(roslyn, text, console, debugMode: false));

        yield return (
            new(ConsoleKey.F12),
            async (text, caret, cancellationToken) => LaunchSource(await roslyn.GetSymbolAtIndexAsync(text, caret)));

        yield return (
            new(ConsoleModifiers.Control, ConsoleKey.D),
            (text, caret, cancellationToken) => Task.FromResult<KeyPressCallbackResult?>(new ExitApplicationKeyPress()));
    }

    protected override Task<TextSpan> GetSpanToReplaceByCompletionAsync(string text, int caret, CancellationToken cancellationToken)
        => roslyn.GetSpanToReplaceByCompletionAsync(text, caret, cancellationToken);

    protected override Task<bool> ShouldOpenCompletionWindowAsync(string text, int caret, KeyPress keyPress, CancellationToken cancellationToken)
        => roslyn.ShouldOpenCompletionWindowAsync(text, caret, keyPress, cancellationToken);

    protected override async Task<IReadOnlyList<CompletionItem>> GetCompletionItemsAsync(string text, int caret, TextSpan spanToBeReplaced, CancellationToken cancellationToken)
    {
        var completions = await roslyn.CompleteAsync(text, caret).ConfigureAwait(false);
        var commitKeybinding = CreateCommitRuleForUserKeybinding(configuration.KeyBindings.CommitCompletion);
        return completions
            .OrderByDescending(i => i.Item.Rules.MatchPriority)
            .ThenBy(i => i.Item.SortText)
            .Select(r => new CompletionItem(
                replacementText: r.Item.DisplayText,
                displayText: r.DisplayText,
                getExtendedDescription: r.GetDescriptionAsync,
                filterText: r.Item.FilterText,
                commitCharacterRules: MergeCommitRules(r.Item.Rules.CommitCharacterRules, commitKeybinding)
            ))
            .ToArray();
    }

    private static CharacterSetModificationRule CreateCommitRuleForUserKeybinding(in KeyPressPatterns commitCompletion)
    {
        var alwaysCommitCharacters = commitCompletion.DefinedPatterns?.Select(key => key.Character).ToArray() ?? Array.Empty<char>();
        return new CharacterSetModificationRule(CharacterSetModificationKind.Add, ImmutableArray.Create(alwaysCommitCharacters));
    }

    // no matter what the roslyn API returns, we should always respect the user's keybindings to commit the completion.
    private static ImmutableArray<CharacterSetModificationRule> MergeCommitRules(
        ImmutableArray<RoslynCharacterSetModificationRule> roslynCompletionRules,
        in CharacterSetModificationRule userDefinedRule)
    {
        var completionRules = roslynCompletionRules
            .Select(r => new CharacterSetModificationRule((CharacterSetModificationKind)r.Kind, r.Characters))
            .ToImmutableArray();

        if (userDefinedRule.Characters.Length == 0)
            return completionRules;

        return completionRules.Insert(0, userDefinedRule);
    }

    protected override async Task<IReadOnlyCollection<FormatSpan>> HighlightCallbackAsync(string text, CancellationToken cancellationToken)
    {
        var classifications = await roslyn.SyntaxHighlightAsync(text).ConfigureAwait(false);
        return classifications.ToFormatSpans();
    }

    protected override async Task<KeyPress> TransformKeyPressAsync(string text, int caret, KeyPress keyPress, CancellationToken cancellationToken)
    {
        // user submitted the prompt but it's incomplete. Insert a newline automatically with the correct level of indentation.
        if (keyPress.ConsoleKeyInfo.Key == ConsoleKey.Enter &&
            keyPress.ConsoleKeyInfo.Modifiers == default &&
            configuration.KeyBindings.SubmitPrompt.Matches(keyPress.ConsoleKeyInfo) &&
            !await roslyn.IsTextCompleteStatementAsync(text).ConfigureAwait(false))
        {
            return NewLineWithIndentation(GetSmartIndentationLevel(text, caret));
        }

        // user pressed e.g. shift-enter to insert a newline.
        if (configuration.KeyBindings.NewLine.Matches(keyPress.ConsoleKeyInfo))
        {
            var indentationLevel = GetSmartIndentationLevel(text, caret);
            return indentationLevel == 0 ? keyPress : NewLineWithIndentation(indentationLevel);
        }

        return keyPress;

        static int GetSmartIndentationLevel(string text, int caret)
        {
            int openBraces = 0;
            var end = Math.Min(text.Length, caret);
            for (int i = 0; i < end; i++)
            {
                var c = text[i];
                if (c == '{') ++openBraces;
                if (c == '}') --openBraces;
            }
            return openBraces;
        }

        static KeyPress NewLineWithIndentation(int indentation) =>
            new(ConsoleKey.Insert.ToKeyInfo('\0', shift: true), "\n" + new string('\t', indentation));
    }

    protected override Task<bool> ConfirmCompletionCommit(string text, int caret, KeyPress keyPress, CancellationToken cancellationToken)
        => roslyn.ConfirmCompletionCommit(text, caret, keyPress, cancellationToken);

    protected override async Task<(string Text, int Caret)> FormatInput(string text, int caret, KeyPress keyPress, CancellationToken cancellationToken)
    {
        var keyChar = keyPress.ConsoleKeyInfo.KeyChar;

        if (caret > 0)
        {
            switch (keyChar)
            {
                case ';' or '}':
                    return await roslyn.FormatInput(text, caret, formatParentNodeOnly: false, cancellationToken).ConfigureAwait(false);
                case '{':
                    return await roslyn.FormatInput(text, caret, formatParentNodeOnly: true, cancellationToken).ConfigureAwait(false);
                default:
                    break;
            }
        }

        return (text, caret);
    }

    protected override Task<(IReadOnlyList<OverloadItem>, int ArgumentIndex)> GetOverloadsAsync(string text, int caret, CancellationToken cancellationToken)
        => roslyn.GetOverloadsAsync(text, caret, cancellationToken);

    private static async Task<KeyPressCallbackResult?> Disassemble(RoslynServices roslyn, string text, IConsoleEx console, bool debugMode)
    {
        var result = await roslyn.ConvertToIntermediateLanguage(text, debugMode);

        switch (result)
        {
            case EvaluationResult.Success success:
                var ilCode = success.ReturnValue.ToString();
                var output = Prompt.RenderAnsiOutput(ilCode, Array.Empty<FormatSpan>(), console.BufferWidth);
                return new KeyPressCallbackResult(text, output);
            case EvaluationResult.Error err:
                return new KeyPressCallbackResult(text, AnsiColor.Red.GetEscapeSequence() + err.Exception.Message + AnsiEscapeCodes.Reset);
            default:
                // this should never happen, as the disassembler cannot be cancelled.
                throw new InvalidOperationException("Could not process disassembly result");
        }
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
        if (type.Url is not null)
        {
            LaunchBrowser(type.Url);
        }
        else if (type != SymbolResult.Unknown && type.SymbolDisplay is not null)
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

/// <summary>
/// Used when the user presses an "exit application" key combo (ctrl-d) to instruct the main REPL loop to end.
/// </summary>
internal sealed class ExitApplicationKeyPress : KeyPressCallbackResult
{
    public ExitApplicationKeyPress()
        : base(string.Empty, null)
    { }
}
