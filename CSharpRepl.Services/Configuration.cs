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
    
    public const string KeyBindingPatternDescription =
        "Key pattern is a key with optional modifiers (Alt/Shift/Control) e.g. 'Enter', 'Control+A'";
  
    /// <summary>
    /// The directory where csharprepl stores its config file, prompt history, and the (potentially large)
    /// NuGet package and symbol caches.
    /// </summary>
    /// <remarks>
    /// On Windows this historically lived in the roaming profile (%APPDATA%>), but new csharprepl installations use
    /// the local profile (%LOCALAPPDATA%>) instead. To avoid orphaning existing users' configuration and caches, we
    /// keep using the roaming location when it already exists.
    /// </remarks>
    public static readonly string ApplicationDirectory = GetApplicationDirectory(
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), ".csharprepl"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), ".csharprepl"),
        Directory.Exists,
        OperatingSystem.IsWindows());

    internal static string GetApplicationDirectory(string roamingDirectory, string localDirectory, Func<string, bool> directoryExists, bool isWindows)
        => !isWindows || directoryExists(roamingDirectory) ? roamingDirectory : localDirectory;

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
    /// When set (via <c>csharprepl connect &lt;pid&gt;</c>), the REPL connects to the connector hosted in that
    /// target process and evaluates submissions there instead of constructing a local script engine.
    /// </summary>
    public int? ConnectProcessId { get; }
    public string? LoadScript { get; }
    public string[] LoadScriptArgs { get; }
    /// <summary>
    /// Output to render before exiting (help, version, usage, <c>connect init</c> exports, ...). Spectre
    /// word-wraps to the console width; for machine-consumable output that must not wrap, supply a
    /// <see cref="PlainText"/>.
    /// </summary>
    public IRenderable? OutputForEarlyExit { get; }
    public AICompletionConfiguration? AICompletionConfiguration { get; }
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
        int? connectProcessId = null,
        int tabSize = 4,
        string? loadScript = null,
        string[]? loadScriptArgs = null,
        IRenderable? outputForEarlyExit = null,
        string[]? triggerCompletionListKeyPatterns = null,
        string[]? newLineKeyPatterns = null,
        string[]? submitPromptKeyPatterns = null,
        string[]? submitPromptDetailedKeyPatterns = null,
        AICompletionConfiguration? aiCompletionConfiguration = null,
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

        ConnectProcessId = connectProcessId;

        // In connect mode, default the prompt to the target's pid (e.g. "1234> ") so it's obvious submissions
        // run remotely. A user-supplied --prompt still wins.
        if (connectProcessId is { } pid && promptMarkup == PromptDefault)
        {
            promptMarkup = $"{pid}> ";
        }

        if (FormattedStringParser.TryParse(promptMarkup, out var prompt))
        {
            Prompt = prompt;
        }
        else
        {
            Console.Error.WriteLine($"{AnsiColor.Red.GetEscapeSequence()}Unable to parse '{promptMarkup}' markup. Default prompt '{PromptDefault}' will be used.{AnsiEscapeCodes.Reset}");
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
        AICompletionConfiguration = aiCompletionConfiguration;
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
                throw new ArgumentException($"Unable to parse '{part}'. {KeyBindingPatternDescription}", nameof(keyPattern));
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

        static void Throw() => throw new ArgumentException(KeyBindingPatternDescription, nameof(keyPattern));

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

/// <summary>
/// Resolved configuration for the AI code-completion feature. Provider-agnostic: <see cref="Endpoint"/>
/// (an OpenAI-compatible base URL, or <see langword="null"/> for the OpenAI default) plus <see cref="Model"/>
/// and <see cref="ApiKey"/> select the provider. Built by <see cref="Completion.AICompleteService.CreateConfiguration"/>.
/// </summary>
public class AICompletionConfiguration
{
    public AICompletionConfiguration(string? apiKey, string? endpoint, string model, string prompt, int historyCount)
    {
        ApiKey = apiKey;
        Endpoint = endpoint;
        Model = model;
        Prompt = prompt;
        HistoryCount = historyCount;
    }

    public string? ApiKey { get; }
    public string? Endpoint { get; }
    public string Model { get; }
    public string Prompt { get; }
    public int HistoryCount { get; }
}