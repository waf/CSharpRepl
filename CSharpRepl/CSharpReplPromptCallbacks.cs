// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Buffers;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CSharpRepl.Repls;
using CSharpRepl.Services;
using CSharpRepl.Services.Completion;
using CSharpRepl.Services.Remote.Commands;
using CSharpRepl.Services.Roslyn;
using CSharpRepl.Services.Roslyn.Scripting;
using CSharpRepl.Services.SymbolExploration;
using CSharpRepl.Services.SyntaxHighlighting;
using Microsoft.CodeAnalysis.Classification;
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
internal class CSharpReplPromptCallbacks(IConsoleService console, RoslynServices roslyn, Configuration configuration, Func<string, KeyPressCallbackResult?>? launchBrowser = null) : PromptCallbacks
{
    private const string lowercaseLetters = "abcdefghijklmnopqrstuvwxyz";
    private static SearchValues<char> lowercaseSearchValues = SearchValues.Create(lowercaseLetters);
    private readonly AICompleteService aiComplete = new AICompleteService(configuration.AICompletionConfiguration);

    // Built once per session because the keyword glyph depends on configuration.UseUnicode, which is fixed.
    private readonly IReadOnlyCollection<CompletionItem> replKeywordCompletionItems = ReplKeywordCompletionItems.Build(configuration.UseUnicode);

    // How the F1/Ctrl+F1/F12 keybindings open a URL. Defaults to actually launching the browser; tests inject a no-op.
    private readonly Func<string, KeyPressCallbackResult?> launchBrowser = launchBrowser ?? LaunchBrowser;

    protected override IEnumerable<(KeyPressPattern Pattern, KeyPressCallbackAsync Callback)> GetKeyPressCallbacks()
    {
        yield return (
            new(ConsoleKey.F1),
            async (text, caret, cancellationToken) => LaunchDocumentation(await roslyn.GetSymbolAtIndexAsync(text, caret), configuration.Culture));

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
            new(ConsoleKey.F8),
            (text, caret, cancellationToken) => Decompile(roslyn, text, console, debugMode: true));

        yield return (
            new(ConsoleModifiers.Control, ConsoleKey.F8),
            (text, caret, cancellationToken) => Decompile(roslyn, text, console, debugMode: false));

        yield return (
            new(ConsoleKey.F12),
            async (text, caret, cancellationToken) => LaunchSource(await roslyn.GetSymbolAtIndexAsync(text, caret)));

        yield return (
            new(ConsoleModifiers.Control | ConsoleModifiers.Alt, ConsoleKey.Spacebar),
            (text, caret, cancellationToken) => AICompleteAsync(text, caret, cancellationToken));

        yield return (
            new(ConsoleModifiers.Control, ConsoleKey.D),
            (text, caret, cancellationToken) => Task.FromResult<KeyPressCallbackResult?>(new ExitApplicationKeyPress()));
    }

    private async Task<KeyPressCallbackResult?> AICompleteAsync(string text, int caret, CancellationToken cancellationToken)
    {
        var submissions = await roslyn.GetPreviousSubmissionsAsync();
        var completion = aiComplete.CompleteAsync(submissions, text, caret, cancellationToken);
        return new StreamingInputCallbackResult(completion);
    }

    protected override Task<TextSpan> GetSpanToReplaceByCompletionAsync(string text, int caret, CancellationToken cancellationToken)
        => roslyn.IsConnectMode && TryGetConnectorCommandSpan(text, caret, out var commandSpan)
            ? Task.FromResult(commandSpan)
            : roslyn.GetSpanToReplaceByCompletionAsync(text, caret, cancellationToken);

    protected override Task<bool> ShouldOpenCompletionWindowAsync(string text, int caret, KeyPress keyPress, CancellationToken cancellationToken)
        => roslyn.IsConnectMode && TryGetConnectorCommandSpan(text, caret, out _)
            ? Task.FromResult(true)
            : roslyn.ShouldOpenCompletionWindowAsync(text, caret, keyPress, cancellationToken);

    protected override async Task<IReadOnlyList<CompletionItem>> GetCompletionItemsAsync(string text, int caret, TextSpan spanToBeReplaced, CancellationToken cancellationToken)
    {
        return await GetCompletionItemsCoreAsync(text, caret, cancellationToken).ConfigureAwait(false);
    }

    // Made internal for testing
    internal async Task<IReadOnlyList<CompletionItem>> GetCompletionItemsCoreAsync(string text, int caret, CancellationToken cancellationToken = default)
    {
        // In connect mode, when the user is typing a #replace/#wrap/#patches/#revert command name, offer those
        // commands (with help text) exclusively: the bad-directive line has no useful Roslyn completions, and #r
        // file-path completion isn't meaningful in a remote session.
        if (roslyn.IsConnectMode && TryGetConnectorCommandSpan(text, caret, out _))
        {
            return ConnectorCommandCompletionItems.AllItems;
        }

        var replKeywordCompletions = GetReplKeywordCompletions();

        var completions = await roslyn.CompleteAsync(text, caret, cancellationToken).ConfigureAwait(false);
        return replKeywordCompletions
            .Concat(completions
                .OrderByDescending(i => i.Item.Rules.MatchPriority)
                .ThenBy(i => i.Item.SortText)
                .Select(CreatePrettyPromptCompletionItem))
            .ToArray();

        IEnumerable<CompletionItem> GetReplKeywordCompletions()
        {
            var trimmed = text.AsSpan().Trim();
            const int largestKeywordLength = 5;
            if (trimmed.Length > largestKeywordLength)
            {
                return [];
            }

            Span<char> lowercaseBuffer = stackalloc char[largestKeywordLength];
            trimmed.ToLowerInvariant(lowercaseBuffer);
            var lowercaseTrimmed = lowercaseBuffer.TrimEnd('\0');
            if (lowercaseTrimmed.ContainsAnyExcept(lowercaseSearchValues))
            {
                return [];
            }

            return replKeywordCompletionItems;
        }
    }

    internal CompletionItem CreatePrettyPromptCompletionItem(CompletionItemWithDescription r)
    {
        var commitKeybinding = CreateCommitRuleForUserKeybinding(configuration.KeyBindings.CommitCompletion);
        return new CompletionItem(
                replacementText: r.Item.DisplayText,
                displayText: r.DisplayText,
                getExtendedDescription: r.GetDescriptionAsync,
                filterText: r.Item.FilterText,
                commitCharacterRules: MergeCommitRules(r.Item.Rules.CommitCharacterRules, commitKeybinding));
    }

    private static CharacterSetModificationRule CreateCommitRuleForUserKeybinding(in KeyPressPatterns commitCompletion)
    {
        var alwaysCommitCharacters = commitCompletion.DefinedPatterns?.Select(key => key.Character).ToArray() ?? [];
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
        var replKeywordSpan = HighlightReplKeyword(text);
        if (replKeywordSpan is not null)
        {
            return [replKeywordSpan.Value];
        }

        var classifications = await roslyn.SyntaxHighlightAsync(text).ConfigureAwait(false);
        return classifications.ToFormatSpans();
    }

    private static FormatSpan? HighlightReplKeyword(string text)
    {
        var trimmed = text.Trim().ToLowerInvariant();
        switch (trimmed)
        {
            case ReadEvalPrintLoop.Keywords.HelpText:
            case "#help":
                return FullSpanWithColor(ReadEvalPrintLoop.Keywords.HelpInfo.Color);

            case ReadEvalPrintLoop.Keywords.ExitText:
                return FullSpanWithColor(ReadEvalPrintLoop.Keywords.ExitInfo.Color);

            case ReadEvalPrintLoop.Keywords.ClearText:
                return FullSpanWithColor(ReadEvalPrintLoop.Keywords.ClearInfo.Color);
        }

        return null;

        FormatSpan FullSpanWithColor(AnsiColor color)
        {
            return EntireWordFormatSpan(text, color);
        }
    }

    protected override async Task<KeyPress> TransformKeyPressAsync(string text, int caret, KeyPress keyPress, CancellationToken cancellationToken)
    {
        // User submitted the prompt but it's incomplete. Insert a newline automatically with the correct level of indentation.
        // A single-line statement the user didn't terminate (e.g. `int i = 0`) counts as submittable, we'll auto-insert the semicolon for them.
        if (keyPress.ConsoleKeyInfo.Key == ConsoleKey.Enter &&
            keyPress.ConsoleKeyInfo.Modifiers == default &&
            configuration.KeyBindings.SubmitPrompt.Matches(keyPress.ConsoleKeyInfo) &&
            !await roslyn.IsTextCompleteStatementAsync(text).ConfigureAwait(false) &&
            roslyn.TryAutoInsertSemicolon(text) is null)
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
            bool inSingleLineComment = false;
            bool inMultiLineComment = false;
            bool inString = false;
            bool inChar = false;
            bool escape = false;

            for (int i = 0; i < Math.Min(text.Length, caret); i++)
            {
                char c = text[i];
                char prev = i > 0 ? text[i - 1] : '\0';

                if (inSingleLineComment)
                {
                    if (c == '\n') inSingleLineComment = false;
                }
                else if (inMultiLineComment)
                {
                    if (prev == '*' && c == '/') inMultiLineComment = false;
                }
                else if (inString)
                {
                    if (!escape && c == '"') inString = false;
                    escape = c == '\\' && !escape;
                }
                else if (inChar)
                {
                    if (!escape && c == '\'') inChar = false;
                    escape = c == '\\' && !escape;
                }
                else
                {
                    if (prev == '/' && c == '/') inSingleLineComment = true;
                    else if (prev == '/' && c == '*') inMultiLineComment = true;
                    else if (c == '"') inString = true;
                    else if (c == '\'') inChar = true;
                    else if (c == '{') openBraces++;
                    else if (c == '}') openBraces--;
                }
            }

            return Math.Max(0, openBraces);
        }

        static KeyPress NewLineWithIndentation(int indentation) =>
            new(ConsoleKey.Insert.ToKeyInfo('\0', shift: true), "\n" + new string('\t', indentation));
    }

    protected override Task<bool> ConfirmCompletionCommit(string text, int caret, KeyPress keyPress, CancellationToken cancellationToken)
    {
        // When a REPL command (exit/clear/help) is already fully typed and the user presses the submit key,
        // committing the open completion would just re-insert the identical text — a no-op that swallows the
        // Enter and forces a second press to actually run the command. Decline the commit so a single Enter
        // submits straight away. Partially-typed input (e.g. "exi") still commits, completing it to the keyword.
        if (configuration.KeyBindings.SubmitPrompt.Matches(keyPress.ConsoleKeyInfo) &&
            ReplKeywordCompletionItems.IsFullyTypedKeyword(text))
        {
            return Task.FromResult(false);
        }

        return roslyn.ConfirmCompletionCommit(text, caret, keyPress, cancellationToken);
    }

    protected override async Task<(string Text, int Caret)> FormatInput(string text, int caret, KeyPress keyPress, CancellationToken cancellationToken)
    {
        var keyChar = keyPress.ConsoleKeyInfo.KeyChar;

        // user submitted the prompt but it's incomplete. Auto-insert a semicolon.
        if (configuration.KeyBindings.SubmitPrompt.Matches(keyPress.ConsoleKeyInfo) &&
            roslyn.TryAutoInsertSemicolon(text) is string textWithSemicolon)
        {
            return await roslyn.FormatInput(textWithSemicolon, textWithSemicolon.Length, formatParentNodeOnly: false, cancellationToken).ConfigureAwait(false);
        }

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

    private static async Task<KeyPressCallbackResult?> Disassemble(RoslynServices roslyn, string text, IConsoleService console, bool debugMode)
    {
        var result = await roslyn.ConvertToIntermediateLanguage(text, debugMode);

        switch (result)
        {
            case EvaluationResult.Success success:
                var (ilCode, highlights) = success.ReturnValue.Value is FormattedString formatted
                    ? (formatted.Text ?? string.Empty, (IReadOnlyCollection<FormatSpan>)formatted.FormatSpans.ToArray())
                    : (success.ReturnValue.ToString() ?? string.Empty, []);
                var output = Prompt.RenderAnsiOutput(ilCode, highlights, console.BufferWidth);
                return new KeyPressCallbackResult(text, output);
            case EvaluationResult.Error err:
                return RenderError(text, err.Exception.Message);
            default:
                // this should never happen, as the disassembler cannot be cancelled.
                throw new InvalidOperationException("Could not process disassembly result");
        }
    }

    private static async Task<KeyPressCallbackResult?> Decompile(RoslynServices roslyn, string text, IConsoleService console, bool debugMode)
    {
        var result = await roslyn.ConvertToLoweredCSharp(text, debugMode);

        switch (result)
        {
            case EvaluationResult.Success success:
                var (csharpCode, highlights) = success.ReturnValue.Value is FormattedString formatted
                    ? (formatted.Text ?? string.Empty, (IReadOnlyCollection<FormatSpan>)formatted.FormatSpans.ToArray())
                    : (success.ReturnValue.ToString() ?? string.Empty, []);
                var output = Prompt.RenderAnsiOutput(csharpCode, highlights, console.BufferWidth);
                return new KeyPressCallbackResult(text, output);
            case EvaluationResult.Error err:
                return RenderError(text, err.Exception.Message);
            default:
                // this should never happen, as the decompiler cannot be cancelled.
                throw new InvalidOperationException("Could not process decompilation result");
        }
    }

    // Renders an error message in red unless the user opted out of color (NO_COLOR).
    private static KeyPressCallbackResult RenderError(string text, string message)
        => new(text, PromptConfiguration.HasUserOptedOutFromColor
            ? message
            : AnsiColor.Red.GetEscapeSequence() + message + AnsiEscapeCodes.Reset);

    private KeyPressCallbackResult? LaunchDocumentation(SymbolResult type, CultureInfo culture)
    {
        if (type != SymbolResult.Unknown && type.SymbolDisplay is not null)
        {
            launchBrowser($"https://docs.microsoft.com/{culture.Name}/dotnet/api/{type.SymbolDisplay}");
        }

        return null;
    }

    private KeyPressCallbackResult? LaunchSource(SymbolResult type)
    {
        if (type.Url is not null)
        {
            launchBrowser(type.Url);
        }
        else if (type != SymbolResult.Unknown && type.SymbolDisplay is not null)
        {
            launchBrowser($"https://source.dot.net/#q={type.SymbolDisplay}");
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

    private static FormatSpan EntireWordFormatSpan(ReadOnlySpan<char> word, AnsiColor color)
    {
        return new(0, word.Length, color);
    }

    private static FormattedString EntireWordFormatString(string word, AnsiColor color)
    {
        return new(word, EntireWordFormatSpan(word, color));
    }

    // Like EntireWordFormatString, but prepends the keyword kind glyph (uncolored) so REPL commands
    // line up with the keyword completions in the menu. The glyph is empty when not using unicode.
    private static FormattedString KeywordCommandFormatString(ReadEvalPrintLoop.Keywords.KeywordInfo keywordInfo, bool useUnicode)
    {
        var prefix = CompletionItemSymbols.Get(ClassificationTypeNames.Keyword, useUnicode).Prefix;
        return new FormattedString(prefix + keywordInfo.Text, new FormatSpan(prefix.Length, keywordInfo.Text.Length, keywordInfo.Color));
    }

    /// <summary>
    /// True when the caret is within a leading <c>#command-name</c> token (no space yet) — i.e. the user is typing
    /// a connect command such as <c>#replace</c>. <paramref name="span"/> covers the whole <c>#word</c> token so a
    /// committed completion replaces it cleanly, including the leading <c>#</c> (which Roslyn's default word span
    /// excludes). Returns false once a space is typed (that's the argument position, handled by the completion
    /// provider) or for any non-command line.
    /// </summary>
    internal static bool TryGetConnectorCommandSpan(string text, int caret, out TextSpan span)
    {
        span = default;
        if (caret < 0 || caret > text.Length)
        {
            return false;
        }

        var lineStart = caret;
        while (lineStart > 0 && text[lineStart - 1] is not '\n' and not '\r')
        {
            lineStart--;
        }

        var hash = lineStart;
        while (hash < caret && char.IsWhiteSpace(text[hash]))
        {
            hash++;
        }

        if (hash >= caret || text[hash] != '#')
        {
            return false;
        }

        // Everything between '#' and the caret must be the command name: letters only, no space.
        for (var i = hash + 1; i < caret; i++)
        {
            if (!char.IsLetter(text[i]))
            {
                return false;
            }
        }

        // Extend to the end of the word so committing replaces the entire token, not just up to the caret.
        var end = caret;
        while (end < text.Length && char.IsLetter(text[end]))
        {
            end++;
        }

        span = new TextSpan(hash, end - hash);
        return true;
    }

    private static class ConnectorCommandCompletionItems
    {
        // Built from the shared command registry; only the color/formatting is added here.
        public static IReadOnlyList<CompletionItem> AllItems { get; } = [.. ConnectorCommands.All.Select(ToCompletionItem)];

        private static CompletionItem ToCompletionItem(ConnectorCommandInfo info) => new(
            info.Token,
            displayText: EntireWordFormatString(info.Token, AnsiColor.BrightMagenta),
            getExtendedDescription: _ => Task.FromResult(new FormattedString(info.Description)));
    }

    private static class ReplKeywordCompletionItems
    {
        // help/exit/clear are REPL commands rather than C# keywords, but we show them with the keyword
        // glyph so they read as keywords in the completion menu. The word keeps its own color; the
        // glyph stays uncolored (matching how the keyword classification is tinted).
        public static IReadOnlyCollection<CompletionItem> Build(bool useUnicode) =>
        [
            Create(ReadEvalPrintLoop.Keywords.HelpInfo, "Show help and usage information for the C# REPL.", useUnicode),
            Create(ReadEvalPrintLoop.Keywords.ExitInfo, "Exit the REPL. You can also press Ctrl + d.", useUnicode),
            Create(ReadEvalPrintLoop.Keywords.ClearInfo, "Clear the terminal screen.", useUnicode),
        ];

        private static CompletionItem Create(ReadEvalPrintLoop.Keywords.KeywordInfo info, string description, bool useUnicode) => new(
            info.Text,
            displayText: KeywordCommandFormatString(info, useUnicode),
            getExtendedDescription: _ => Task.FromResult(new FormattedString(description)));

        private static readonly HashSet<string> replacementTexts = new(StringComparer.OrdinalIgnoreCase)
        {
            ReadEvalPrintLoop.Keywords.HelpText,
            ReadEvalPrintLoop.Keywords.ExitText,
            ReadEvalPrintLoop.Keywords.ClearText,
        };

        public static bool IsFullyTypedKeyword(string text) => replacementTexts.Contains(text.Trim());
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
