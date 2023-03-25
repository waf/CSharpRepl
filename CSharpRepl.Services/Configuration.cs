// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Diagnostics;
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

    public static readonly IReadOnlyCollection<string> SymbolServers = new[]
    {
        "https://symbols.nuget.org/download/symbols/",
        "http://msdl.microsoft.com/download/symbols/"
    };

    public HashSet<string> References { get; }
    public HashSet<string> Usings { get; }
    public string Framework { get; }
    public bool Trace { get; }
    public Theme Theme { get; }
    public bool UseTerminalPaletteTheme { get; }
    public FormattedString Prompt { get; }
    public bool UseUnicode { get; }
    public bool UsePrereleaseNugets { get; }
    public string? LoadScript { get; }
    public string[] LoadScriptArgs { get; }
    public FormattedString OutputForEarlyExit { get; }
    public OpenAIConfiguration OpenAIConfiguration { get; }
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
        int tabSize = 4,
        string? loadScript = null,
        string[]? loadScriptArgs = null,
        FormattedString outputForEarlyExit = default,
        string[]? triggerCompletionListKeyPatterns = null,
        string[]? newLineKeyPatterns = null,
        string[]? submitPromptKeyPatterns = null,
        string[]? submitPromptDetailedKeyPatterns = null,
        OpenAIConfiguration openAIConfiguration = null)
    {
        References = references?.ToHashSet() ?? new HashSet<string>();
        Usings = usings?.ToHashSet() ?? new HashSet<string>();
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
        TabSize = tabSize;
        LoadScript = loadScript;
        LoadScriptArgs = loadScriptArgs ?? Array.Empty<string>();
        OutputForEarlyExit = outputForEarlyExit;
        OpenAIConfiguration = openAIConfiguration;
        var triggerCompletionList =
            triggerCompletionListKeyPatterns?.Any() == true
            ? ParseKeyPressPatterns(triggerCompletionListKeyPatterns)
            : new KeyPressPatterns(new(ConsoleModifiers.Control, ConsoleKey.Spacebar), new(ConsoleModifiers.Control, ConsoleKey.J));

        var newLine = newLineKeyPatterns?.Any() == true ? ParseKeyPressPatterns(newLineKeyPatterns) : default;

        if (submitPromptKeyPatterns?.Any() != true)
        {
            submitPromptKeyPatterns = new[] { "Enter" };
        }
        if (submitPromptDetailedKeyPatterns?.Any() != true)
        {
            submitPromptDetailedKeyPatterns = new[] { "Ctrl+Enter", "Ctrl+Alt+Enter" };
        }

        var submitPrompt = ParseKeyPressPatterns(submitPromptKeyPatterns.Concat(submitPromptDetailedKeyPatterns).ToArray());
        SubmitPromptDetailedKeys = ParseKeyPressPatterns(submitPromptDetailedKeyPatterns);

        var commitCompletion = new KeyPressPatterns(
            CompletionRules.Default.DefaultCommitCharacters.Select(c => new KeyPressPattern(c))
            .Concat(new KeyPressPattern[] { new(ConsoleKey.Enter), new(ConsoleKey.Tab) })
            .ToArray());

        KeyBindings = new(
            commitCompletion,
            triggerCompletionList,
            newLine,
            submitPrompt,
            triggerOverloadList: new(new KeyPressPattern('('), new KeyPressPattern('['), new KeyPressPattern(','), new KeyPressPattern('<')));
    }

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
    public OpenAIConfiguration(string? apiKey, string prompt, string model, int historyCount, double? temperature, double? topProbability)
    {
        ApiKey = apiKey;
        Prompt = prompt;
        Model = model;
        HistoryCount = historyCount;
        Temperature = temperature;
        TopProbability = topProbability;
    }

    public string? ApiKey { get; }
    public string Prompt { get; }
    public string Model { get; }
    public int HistoryCount { get; }
    public double? Temperature { get; }
    public double? TopProbability { get; }
}