// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using CSharpRepl.Services.Roslyn.References;
using CSharpRepl.Services.Theming;
using Microsoft.CodeAnalysis.Completion;
using PrettyPrompt;
using PrettyPrompt.Consoles;
using PrettyPrompt.Highlighting;
using Spectre.Console.Rendering;

namespace CSharpRepl.Services;

/// <summary>
/// Configuration from command line parameters
/// </summary>
public sealed class Configuration
{
    public const string FrameworkDefault = SharedFramework.NetCoreApp;

    public static readonly string DefaultThemeRelativePath = Path.Combine("themes", "VisualStudio_Dark.json");

    public const string PromptDefault = "> ";

    public static readonly string ApplicationDirectory =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), ".csharprepl");

    public static readonly string ExecutableDirectory =
        Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ??
        Environment.CurrentDirectory;

    public static readonly IReadOnlyCollection<string> SymbolServers =
    [
        "https://symbols.nuget.org/download/symbols/",
        "https://msdl.microsoft.com/download/symbols/"
    ];

    public HashSet<string> References { get; }
    public HashSet<string> Usings { get; }
    public string Framework { get; }
    public bool Trace { get; }
    public Theme Theme { get; }
    public bool UseTerminalPaletteTheme { get; }
    public FormattedString Prompt { get; }
    public bool UseUnicode { get; }
    public bool UsePrereleaseNugets { get; }
    public bool StreamPipedInput { get; set; }

    /// <summary>
    /// C# to evaluate non-interactively (from --eval or --eval-file) before exiting. Null when running
    /// interactively or reading from piped stdin.
    /// </summary>
    public string? EvaluateInput { get; }

    /// <summary>
    /// When set (via <c>csharprepl inspect &lt;pid&gt;</c>), the REPL connects to the inspector hosted in that
    /// target process and evaluates submissions there instead of constructing a local script engine.
    /// </summary>
    public int? InspectProcessId { get; }
    public string? LoadScript { get; }
    public string[] LoadScriptArgs { get; }
    public IRenderable? OutputForEarlyExit { get; }

    /// <summary>
    /// Plain text to write to standard output (unwrapped, no Spectre rendering) before exiting — used for
    /// machine-consumable output such as the <c>inspect init</c> shell exports, where word-wrapping a long
    /// path would corrupt a copy-paste or a pipe into the shell.
    /// </summary>
    public string? EarlyExitPlainText { get; }
    public OpenAIConfiguration? OpenAIConfiguration { get; }
    public int TabSize { get; }

    public KeyBindings KeyBindings { get; }
    public KeyPressPatterns SubmitPromptDetailedKeys { get; }

    public Configuration(
        string[]? references = null,
        string[]? usings = null,
        string? framework = null,
        bool trace = false,
        string? theme = null,
        bool useTerminalPaletteTheme = false,
        string promptMarkup = PromptDefault,
        bool useUnicode = false,
        bool usePrereleaseNugets = false,
        bool streamPipedInput = false,
        string? evaluateInput = null,
        int? inspectProcessId = null,
        int tabSize = 4,
        string? loadScript = null,
        string[]? loadScriptArgs = null,
        IRenderable? outputForEarlyExit = null,
        string? earlyExitPlainText = null,
        string[]? triggerCompletionListKeyPatterns = null,
        string[]? newLineKeyPatterns = null,
        string[]? submitPromptKeyPatterns = null,
        string[]? submitPromptDetailedKeyPatterns = null,
        OpenAIConfiguration? openAIConfiguration = null,
        string? cultureName = null)
    {
        References = references?.ToHashSet() ?? [];
        Usings = usings?.ToHashSet() ?? [];
        Framework = framework ?? FrameworkDefault;
        Trace = trace;
        UseTerminalPaletteTheme = useTerminalPaletteTheme;

        if (useTerminalPaletteTheme)
        {
            Theme = Theme.DefaultTheme;
        }
        else
        {
            if (string.IsNullOrEmpty(theme)) theme = DefaultThemeRelativePath;
            bool themeExists = File.Exists(theme);
            if (!themeExists)
            {
                if (!Path.IsPathFullyQualified(theme))
                {
                    theme = Path.Combine(ExecutableDirectory, theme);
                    themeExists = File.Exists(theme);
                }
            }

            if (themeExists)
            {
                Theme = JsonSerializer.Deserialize<Theme>(
                             File.ReadAllText(theme),
                             new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }
                           ) ?? Theme.DefaultTheme;
            }
            else
            {
                Console.Error.WriteLine($"{AnsiColor.Red.GetEscapeSequence()}Unable to locate theme file '{theme}'. Defaut theme with terminal palette colors will be used.{AnsiEscapeCodes.Reset}");
                Theme = Theme.DefaultTheme;
            }
        }

        InspectProcessId = inspectProcessId;

        // In inspect mode, default the prompt to the target's pid (e.g. "1234> ") so it's obvious submissions
        // run remotely. A user-supplied --prompt still wins.
        if (inspectProcessId is { } pid && promptMarkup == PromptDefault)
        {
            promptMarkup = $"{pid}> ";
        }

        if (FormattedStringParser.TryParse(promptMarkup, out var prompt))
        {
            Prompt = prompt;
        }
        else
        {
            Console.Error.WriteLine($"{AnsiColor.Red.GetEscapeSequence()}Unable to parse '{prompt}' markup. Defaut prompt '{PromptDefault}' will be used.{AnsiEscapeCodes.Reset}");
            Prompt = PromptDefault;
        }

        UseUnicode = useUnicode;
        UsePrereleaseNugets = usePrereleaseNugets;
        StreamPipedInput = streamPipedInput;
        EvaluateInput = evaluateInput;
        TabSize = tabSize;
        LoadScript = loadScript;
        LoadScriptArgs = loadScriptArgs ?? [];
        OutputForEarlyExit = outputForEarlyExit;
        EarlyExitPlainText = earlyExitPlainText;
        OpenAIConfiguration = openAIConfiguration;
        var triggerCompletionList =
            triggerCompletionListKeyPatterns?.Any() == true
            ? ParseKeyPressPatterns(triggerCompletionListKeyPatterns)
            : new KeyPressPatterns(new(ConsoleModifiers.Control, ConsoleKey.Spacebar), new(ConsoleModifiers.Control, ConsoleKey.J));

        var newLine = newLineKeyPatterns?.Any() == true ? ParseKeyPressPatterns(newLineKeyPatterns) : default;

        if (submitPromptKeyPatterns?.Any() != true)
        {
            submitPromptKeyPatterns = ["Enter"];
        }
        if (submitPromptDetailedKeyPatterns?.Any() != true)
        {
            submitPromptDetailedKeyPatterns = ["Ctrl+Enter", "Ctrl+Alt+Enter"];
        }

        var submitPrompt = ParseKeyPressPatterns(submitPromptKeyPatterns.Concat(submitPromptDetailedKeyPatterns).ToArray());
        SubmitPromptDetailedKeys = ParseKeyPressPatterns(submitPromptDetailedKeyPatterns);

        var commitCompletion = new KeyPressPatterns(
            CompletionRules.Default.DefaultCommitCharacters.Select(c => new KeyPressPattern(c))
            .Concat([new(ConsoleKey.Enter), new(ConsoleKey.Tab)])
            .ToArray());

        KeyBindings = new(
            commitCompletion,
            triggerCompletionList,
            newLine,
            submitPrompt,
            triggerOverloadList: new(new KeyPressPattern('('), new KeyPressPattern('['), new KeyPressPattern(','), new KeyPressPattern('<')));

        Culture = string.IsNullOrWhiteSpace(cultureName) ? CultureInfo.CurrentUICulture : CultureInfo.GetCultureInfo(cultureName, true);
    }

    public CultureInfo Culture { get; }

    private static KeyPressPatterns ParseKeyPressPatterns(string[] keyPatterns)
        => keyPatterns.Select(ParseKeyPressPattern).ToArray();

    internal static KeyPressPattern ParseKeyPressPattern(string keyPattern)
    {
        if (string.IsNullOrEmpty(keyPattern)) return default;

        const string GeneralInfo = "Key pattern must contain one key with optional modifiers (Alt/Shift/Control). E.g. 'Enter', 'Control+A', '(', 'Alt+.', ...";

        ConsoleKey? key = null;
        char? keyChar = null;
        ConsoleModifiers modifiers = default;
        foreach (var part in keyPattern.Split('+'))
        {
            if (Enum.TryParse<ConsoleKey>(part, ignoreCase: true, out var parsedKey))
            {
                if (key != null) Throw();
                key = parsedKey;
            }
            else if (TryParseConsoleModifiers(part, out var parsedModifier))
            {
                modifiers |= parsedModifier;
            }
            else if (part.Length == 1)
            {
                if (keyChar != null) Throw();
                keyChar = part[0];
            }
            else
            {
                throw new ArgumentException($"Unable to parse '{part}'. {GeneralInfo}", nameof(keyPattern));
            }
        }

        if (!(key.HasValue ^ keyChar.HasValue)) Throw();

        if (key.HasValue)
        {
            return new KeyPressPattern(modifiers, key.Value);
        }
        else
        {
            Debug.Assert(keyChar != null);
            if (modifiers != default) throw new ArgumentException($"Key patterns currently does not support '{keyChar.Value}' with modifiers.", nameof(keyPattern));
            return new KeyPressPattern(keyChar.Value);
        }

        static void Throw() => throw new ArgumentException(GeneralInfo, nameof(keyPattern));

        static bool TryParseConsoleModifiers(string text, out ConsoleModifiers result)
        {
            if (Enum.TryParse(text, ignoreCase: true, out result))
            {
                return true;
            }
            else if (text.Equals("ctrl", StringComparison.OrdinalIgnoreCase))
            {
                result = ConsoleModifiers.Control;
                return true;
            }
            result = default;
            return false;
        }
    }
}

public class OpenAIConfiguration
{
    public OpenAIConfiguration(string? apiKey, string prompt, string model, int historyCount)
    {
        ApiKey = apiKey;
        Prompt = prompt;
        Model = model;
        HistoryCount = historyCount;
    }

    public string? ApiKey { get; }
    public string Prompt { get; }
    public string Model { get; }
    public int HistoryCount { get; }
}