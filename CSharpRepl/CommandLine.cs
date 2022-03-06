// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.CommandLine;
using System.CommandLine.Builder;
using System.CommandLine.IO;
using System.CommandLine.Parsing;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using CSharpRepl.Services;
using CSharpRepl.Services.Roslyn.References;
using static System.Environment;

namespace CSharpRepl;

/// <summary>
/// Parses command line arguments using System.CommandLine.
/// Includes support for dotnet-suggest.
/// </summary>
internal static class CommandLine
{
    private const string DisableFurtherOptionParsing = "--";

    private static readonly Option<string[]?> References = new(
        aliases: new[] { "--reference", "-r", "/r" },
        description: "Reference assemblies, nuget packages, and csproj files. Can be specified multiple times."
    );

    private static readonly Option<string[]?> Usings = new Option<string[]?>(
        aliases: new[] { "--using", "-u", "/u" },
        description: "Add using statement. Can be specified multiple times."
    ).AddSuggestions(GetAvailableUsings);

    private static readonly Option<string> Framework = new Option<string>(
        aliases: new[] { "--framework", "-f", "/f" },
        description: "Reference a shared framework.",
        getDefaultValue: () => Configuration.FrameworkDefault
    ).AddSuggestions(SharedFramework.SupportedFrameworks);

    private static readonly Option<string> Theme = new(
        aliases: new[] { "--theme", "-t", "/t" },
        description: "Read a theme file for syntax highlighting. Respects the NO_COLOR standard.",
        getDefaultValue: () => Configuration.DefaultThemeRelativePath
    );

    private static readonly Option<bool> UseTerminalPaletteTheme = new(
        aliases: new[] { "--useTerminalPaletteTheme" },
        description: "Ignores theme loaded from file and uses default theme with terminal palette colors. Respects the NO_COLOR standard."
    );

    private static readonly Option<string> Prompt = new(
        aliases: new[] { "--prompt" },
        description: "Formatted prompt string.",
        getDefaultValue: () => Configuration.PromptDefault
    );

    private static readonly Option<bool> Trace = new(
        aliases: new[] { "--trace" },
        description: "Produce a trace file in the current directory, for CSharpRepl bug reports."
    );

    private static readonly Option<bool> Help = new(
        aliases: new[] { "--help", "-h", "-?", "/h", "/?" },
        description: "Show this help and exit."
    );

    private static readonly Option<bool> Version = new(
        aliases: new[] { "--version", "-v", "/v" },
        description: "Show version number and exit."
    );

    private static readonly Option<string[]?> CommitCompletionKeyBindings = new(
        aliases: new[] { "--commitCompletionKeys" },
        description: "Set up key bindings for commit completion item. Can be specified multiple times."
    );

    private static readonly Option<string[]?> TriggerCompletionListKeyBindings = new(
        aliases: new[] { "--triggerCompletionListKeys" },
        description: "Set up key bindings for trigger completion list. Can be specified multiple times."
    );

    private static readonly Option<string[]?> NewLineKeyBindings = new(
        aliases: new[] { "--newLineKeys" },
        description: "Set up key bindings for new line character insertion. Can be specified multiple times."
    );

    private static readonly Option<string[]?> SubmitPromptKeyBindings = new(
        aliases: new[] { "--submitPromptKeys" },
        description: "Set up key bindings for the submit of current prompt. Can be specified multiple times."
    );

    private static readonly Option<string[]?> SubmitPromptDetailedKeyBindings = new(
        aliases: new[] { "--submitPromptDetailedKeys" },
        description: "Set up key bindings for the submit of current prompt with detailed output. Can be specified multiple times."
    );

    public static Configuration Parse(string[] args)
    {
        var parseArgs = RemoveScriptArguments(args).ToArray();

        Framework.AddValidator(r =>
        {
            if (!r.Children.Any()) return null;

            string frameworkValue = r.GetValueOrDefault<string>() ?? string.Empty;
            return SharedFramework.SupportedFrameworks.Any(f => frameworkValue.StartsWith(f, StringComparison.OrdinalIgnoreCase))
                ? null // success
                : "Unrecognized --framework value";
        });

        var commandLine =
            new CommandLineBuilder(
                new RootCommand("C# REPL")
                {
                    References, Usings, Framework, Theme, UseTerminalPaletteTheme, Prompt, Trace, Help, Version,
                    CommitCompletionKeyBindings, TriggerCompletionListKeyBindings, NewLineKeyBindings, SubmitPromptKeyBindings, SubmitPromptDetailedKeyBindings
                }
            )
            .UseSuggestDirective() // support autocompletion via dotnet-suggest
            .Build()
            .Parse(parseArgs);

        if (ShouldExitEarly(commandLine, out var text))
        {
            return new Configuration(outputForEarlyExit: text);
        }
        if (commandLine.Errors.Count > 0)
        {
            throw new InvalidOperationException(string.Join(NewLine, commandLine.Errors));
        }

        var config = new Configuration(
            references: commandLine.ValueForOption(References),
            usings: commandLine.ValueForOption(Usings),
            framework: commandLine.ValueForOption(Framework),
            loadScript: ProcessScriptArguments(args),
            loadScriptArgs: commandLine.UnparsedTokens.ToArray(),
            theme: commandLine.ValueForOption(Theme),
            useTerminalPaletteTheme: commandLine.ValueForOption(UseTerminalPaletteTheme),
            promptMarkup: commandLine.ValueForOption(Prompt) ?? Configuration.PromptDefault,
            trace: commandLine.ValueForOption(Trace),
            commitCompletionKeyPatterns: commandLine.ValueForOption(CommitCompletionKeyBindings),
            triggerCompletionListKeyPatterns: commandLine.ValueForOption(TriggerCompletionListKeyBindings),
            newLineKeyPatterns: commandLine.ValueForOption(NewLineKeyBindings),
            submitPromptKeyPatterns: commandLine.ValueForOption(SubmitPromptKeyBindings),
            submitPromptDetailedKeyPatterns: commandLine.ValueForOption(SubmitPromptDetailedKeyBindings)
        );

        return config;
    }

    private static bool ShouldExitEarly(ParseResult commandLine, [NotNullWhen(true)] out string? text)
    {
        if (commandLine.Directives.Any())
        {
            // this is just for dotnet-suggest directive processing. Invoking should write to stdout
            // and should not start the REPL. It's a feature of System.CommandLine.
            var console = new TestConsole();
            commandLine.Invoke(console);
            text = console.Out.ToString() ?? string.Empty;
            return true;
        }
        if (commandLine.ValueForOption<bool>("--help"))
        {
            text = GetHelp();
            return true;
        }
        if (commandLine.ValueForOption<bool>("--version"))
        {
            text = GetVersion();
            return true;
        }

        text = null;
        return false;
    }

    /// <summary>
    /// We allow csx files to be specified, sometimes in ambiguous scenarios that
    /// System.CommandLine can't figure out. So we remove it from processing here,
    /// and process it manually in <see cref="ProcessScriptArguments"/>.
    /// </summary>
    private static IEnumerable<string> RemoveScriptArguments(string[] args)
    {
        bool foundIgnore = false;
        foreach (var arg in args)
        {
            foundIgnore |= arg == DisableFurtherOptionParsing;
            if (foundIgnore || !arg.EndsWith(".csx"))
            {
                yield return arg;
            }
        }
    }

    /// <summary>
    /// Reads the contents of any provided script (csx) files.
    /// </summary>
    private static string? ProcessScriptArguments(string[] args)
    {
        var stringBuilder = new StringBuilder();
        foreach (var arg in args)
        {
            if (arg == DisableFurtherOptionParsing) break;
            if (!arg.EndsWith(".csx")) continue;
            if (!File.Exists(arg)) throw new FileNotFoundException($@"Script file ""{arg}"" was not found");
            stringBuilder.AppendLine(File.ReadAllText(arg));
        }
        return stringBuilder.Length == 0 ? null : stringBuilder.ToString();
    }

    /// <summary>
    /// Output of --help
    /// </summary>
    /// <remarks>
    /// System.CommandLine can generate the help text for us, but I think it's less
    /// readable, and the code to configure it ends up being longer than the below string.
    /// </remarks>
    private static string GetHelp() =>
        GetVersion() + NewLine +
        "Usage: csharprepl [OPTIONS] [@response-file.rsp] [script-file.csx] [-- <additional-arguments>]" + NewLine + NewLine +
        "Starts a REPL (read eval print loop) according to the provided [OPTIONS]." + NewLine +
        "These [OPTIONS] can be provided at the command line, or via a [@response-file.rsp]." + NewLine +
        "A [script-file.csx], if provided, will be executed before the prompt starts." + NewLine + NewLine +
        "OPTIONS:" + NewLine +
        $"  -r <dll> or --reference <dll>:              {References.Description}" + NewLine +
        $"  -u <namespace> or --using <namespace>:      {Usings.Description}" + NewLine +
        $"  -f <framework> or --framework <framework>:  {Framework.Description}" + NewLine +
        $"                                              Available shared frameworks: " + NewLine + GetInstalledFrameworks(
        $"                                               ") + NewLine +
        $"  -t <theme.json> or --theme <theme.json>:    {Theme.Description}" + NewLine +
        $"                                              Available default themes: " + NewLine + GetDefaultThemes(
        $"                                               ") + NewLine +
        $"  --useTerminalPaletteTheme:                  {UseTerminalPaletteTheme.Description}" + NewLine +
        $"  --prompt:                                   {Prompt.Description}" + NewLine +
        $"  -v or --version:                            {Version.Description}" + NewLine +
        $"  -h or --help:                               {Help.Description}" + NewLine +
        $"  --commitCompletionKeys <key-binding>:       {CommitCompletionKeyBindings.Description}" + NewLine +
        $"  --triggerCompletionListKeys  <key-binding>: {TriggerCompletionListKeyBindings.Description}" + NewLine +
        $"  --newLineKeys <key-binding>:                {NewLineKeyBindings.Description}" + NewLine +
        $"  --submitPromptKeys <key-binding>:           {SubmitPromptKeyBindings.Description}" + NewLine +
        $"  --submitPromptDetailedKeys <key-binding>:   {SubmitPromptDetailedKeyBindings.Description}" + NewLine +
        $"  --trace:                                    {Trace.Description}" + NewLine + NewLine +
        "@response-file.rsp:" + NewLine +
        "  A file, with extension .rsp, containing the above command line [OPTIONS], one option per line." + NewLine + NewLine +
        "script-file.csx:" + NewLine +
        "  A file, with extension .csx, containing lines of C# to evaluate before starting the REPL." + NewLine +
        "  Arguments to this script can be passed as <additional-arguments> and will be available in a global `args` variable." + NewLine;

    /// <summary>
    /// Get assembly version for usage in --version
    /// </summary>
    private static string GetVersion()
    {
        var product = "C# REPL";
        var version = Assembly
            .GetExecutingAssembly()
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion ?? "unversioned";
        return product + " " + version;
    }

    /// <summary>
    /// In the help text, lists the available frameworks and marks one as default.
    /// </summary>
    private static string GetInstalledFrameworks(string leftPadding)
    {
        var frameworkList = SharedFramework
            .SupportedFrameworks
            .Select(fx => leftPadding + "- " + fx + (fx == Configuration.FrameworkDefault ? " (default)" : ""));
        return string.Join(NewLine, frameworkList);
    }

    private static string GetDefaultThemes(string leftPadding)
    {
        var themesDir = Path.Combine(Configuration.ExecutableDirectory, "themes");
        if (!Directory.Exists(themesDir)) return $"Directory '{themesDir}' not found.";

        var themes = Directory.EnumerateFiles(themesDir)
            .Select(
            t =>
            {
                var themePath = Path.GetRelativePath(Configuration.ExecutableDirectory, t);
                return leftPadding + "- " + themePath + (themePath == Configuration.DefaultThemeRelativePath ? " (default)" : "");
            });
        return string.Join(NewLine, themes);
    }

    /// <summary>
    /// Autocompletions for --using.
    /// </summary>
    private static IEnumerable<string> GetAvailableUsings(ParseResult? parseResult, string? textToMatch)
    {
        if (string.IsNullOrEmpty(textToMatch) || "Syste".StartsWith(textToMatch, StringComparison.OrdinalIgnoreCase))
            return new[] { "System" };

        if (!textToMatch.StartsWith("System", StringComparison.OrdinalIgnoreCase))
            return Array.Empty<string>();

        var runtimeAssemblyPaths = Directory.GetFiles(RuntimeEnvironment.GetRuntimeDirectory(), "*.dll");
        using var mlc = new MetadataLoadContext(new PathAssemblyResolver(runtimeAssemblyPaths));

        var namespaces =
            from assembly in runtimeAssemblyPaths
            from type in GetTypes(assembly)
            where type.IsPublic
                  && type.Namespace is not null
                  && type.Namespace.StartsWith(textToMatch, StringComparison.OrdinalIgnoreCase)
            select type.Namespace;

        return namespaces.Distinct().Take(16).ToArray();

        IEnumerable<Type> GetTypes(string assemblyPath)
        {
            try { return mlc.LoadFromAssemblyPath(assemblyPath).GetTypes(); }
            catch (BadImageFormatException) { return Array.Empty<Type>(); } // handle native DLLs that have no managed metadata.
        }
    }
}
