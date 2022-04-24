// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.CommandLine;
using System.CommandLine.Builder;
using System.CommandLine.IO;
using System.CommandLine.Parsing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security;
using System.Text;
using CSharpRepl.Services;
using CSharpRepl.Services.Roslyn.References;
using CSharpRepl.Services.Theming;
using PrettyPrompt.Highlighting;
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

    private static readonly Option<bool> UseUnicode = new(
        aliases: new[] { "--useUnicode" },
        description: "UTF8 output encoding will be enabled and unicode character will be used (requires terminal support)."
    );

    private static readonly Option<bool> UsePrereleaseNugets = new(
        aliases: new[] { "--usePrereleaseNugets" },
        description: "Determines whether prerelease NuGet versions should be taken into account when searching for the latest package version."
    );

    private static readonly Option<bool> Trace = new(
        aliases: new[] { "--trace" },
        description: "Produce a trace file in the current directory, for CSharpRepl bug reports."
    );

    private static readonly Option<bool> Version = new(
        aliases: new[] { "--version", "-v", "/v" },
        description: "Show version number and exit."
    );

    private static readonly Option<bool> Help = new(
        aliases: new[] { "--help", "-h", "-?", "/h", "/?" },
        description: "Show this help and exit."
    );

    private static readonly Option<int> TabSize = new(
        aliases: new[] { "--tabSize" },
        getDefaultValue: () => 4,
        description: "Width of tab character."
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

    public static Configuration Parse(string[] args, string configFilePath)
    {
        var parseArgs = PreProcessArguments(args, configFilePath).ToArray();

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
                    References, Usings, Framework, Theme, UseTerminalPaletteTheme, Prompt, UseUnicode, UsePrereleaseNugets, Trace, Version, Help, TabSize,
                    TriggerCompletionListKeyBindings, NewLineKeyBindings, SubmitPromptKeyBindings, SubmitPromptDetailedKeyBindings
                }
            )
            .UseSuggestDirective() // support autocompletion via dotnet-suggest
            .Build()
            .Parse(parseArgs);

        if (ShouldExitEarly(commandLine, configFilePath, out var text))
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
            useUnicode: commandLine.ValueForOption(UseUnicode),
            usePrereleaseNugets: commandLine.ValueForOption(UsePrereleaseNugets),
            tabSize: commandLine.ValueForOption(TabSize),
            trace: commandLine.ValueForOption(Trace),
            triggerCompletionListKeyPatterns: commandLine.ValueForOption(TriggerCompletionListKeyBindings),
            newLineKeyPatterns: commandLine.ValueForOption(NewLineKeyBindings),
            submitPromptKeyPatterns: commandLine.ValueForOption(SubmitPromptKeyBindings),
            submitPromptDetailedKeyPatterns: commandLine.ValueForOption(SubmitPromptDetailedKeyBindings)
        );

        return config;
    }

    private static bool ShouldExitEarly(ParseResult commandLine, string configFilePath, out FormattedString text)
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
            text = GetHelp(configFilePath);
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
    /// Adds/removes arguments to the user's provided arguments to handle rsp and csx files.
    /// </summary>
    private static IEnumerable<string> PreProcessArguments(string[] args, string configFilePath)
    {
        // if we're running a dotnet-suggest directive, don't touch the args.
        if (args.FirstOrDefault()?.FirstOrDefault() == '[')
        {
            foreach (var arg in args)
            {
                yield return arg;
            }
            yield break;
        }

        // If the user has a config.rsp file in their app storage directory, we'll load it automatically.
        // This file path is e.g. ~\AppData\Roaming\.csharprepl\config.rsp or ~/.config/.csharprepl/config.rsp
        // https://github.com/dotnet/command-line-api/blob/main/docs/Features-overview.md#response-files
        if (File.Exists(configFilePath))
        {
            yield return "@" + configFilePath;
        }
        else
        {
            CreateDefaultConfigurationFile(configFilePath);
        }

        // We allow csx files to be specified, sometimes in ambiguous scenarios that
        // System.CommandLine can't figure out. So we remove it from processing here,
        // and process it manually in ProcessScriptArguments
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
    private static FormattedString GetHelp(string configFilePath)
    {
        var text = FormattedStringParser.Parse(
            "[underline]Usage[/]: [brightcyan]csharprepl[/] [green][[OPTIONS]][/] [cyan][[@response-file.rsp]][/] [cyan][[script-file.csx]][/] [green][[-- <additional-arguments>]][/]" + NewLine + NewLine +
            "Starts a REPL (read eval print loop) according to the provided [green][[OPTIONS]][/]." + NewLine +
            "These [green][[OPTIONS]][/] can be provided at the command line, or via a [cyan][[@response-file.rsp]][/]." + NewLine +
            "A [cyan][[script-file.csx]][/], if provided, will be executed before the prompt starts." + NewLine + NewLine +
            "[underline]OPTIONS[/]:" + NewLine +
            $"  [green]-r[/] [cyan]<dll>[/] or [green]--reference[/] [cyan]<dll>[/]:              {References.Description}" + NewLine +
            $"  [green]-u[/] [cyan]<namespace>[/] or [green]--using[/] [cyan]<namespace>[/]:      {Usings.Description}" + NewLine +
            $"  [green]-f[/] [cyan]<framework>[/] or [green]--framework[/] [cyan]<framework>[/]:  {Framework.Description}" + NewLine +
            $"                                              Available shared frameworks: " + NewLine + GetInstalledFrameworks(
            $"                                               ") + NewLine +
            $"  [green]-t[/] [cyan]<theme.json>[/] or [green]--theme[/] [cyan]<theme.json>[/]:    {Theme.Description}" + NewLine +
            $"                                              Available default themes: " + NewLine + GetDefaultThemes(
            $"                                               ") + NewLine +
            $"  [green]--useTerminalPaletteTheme[/]:                  {UseTerminalPaletteTheme.Description}" + NewLine +
            $"  [green]--prompt[/]:                                   {Prompt.Description}" + NewLine +
            $"  [green]--useUnicode[/]:                               {UseUnicode.Description}" + NewLine +
            $"  [green]--usePrereleaseNugets[/]:                      {UsePrereleaseNugets.Description}" + NewLine +
            $"  [green]--tabSize[/] [cyan]<width>[/]:                          {TabSize.Description}" + NewLine +
            $"  [green]--triggerCompletionListKeys[/] [cyan]<key-binding>[/]:  {TriggerCompletionListKeyBindings.Description}" + NewLine +
            $"  [green]--newLineKeys[/] [cyan]<key-binding>[/]:                {NewLineKeyBindings.Description}" + NewLine +
            $"  [green]--submitPromptKeys[/] [cyan]<key-binding>[/]:           {SubmitPromptKeyBindings.Description}" + NewLine +
            $"  [green]--submitPromptDetailedKeys[/] [cyan]<key-binding>[/]:   {SubmitPromptDetailedKeyBindings.Description}" + NewLine +
            $"  [green]--trace[/]:                                    {Trace.Description}" + NewLine +
            $"  [green]-v[/] or [green]--version[/]:                            {Version.Description}" + NewLine +
            $"  [green]-h[/] or [green]--help[/]:                               {Help.Description}" + NewLine + NewLine +
            "[cyan]@response-file.rsp[/]:" + NewLine +
            "  A file, with extension .rsp, containing the above command line [green][[OPTIONS]][/], one option per line." + NewLine +
            $"  Command line options will also be loaded from {configFilePath}" + NewLine + NewLine +
            "[cyan]script-file.csx[/]:" + NewLine +
            "  A file, with extension .csx, containing lines of C# to evaluate before starting the REPL." + NewLine +
            "  Arguments to this script can be passed as [green]<additional-arguments>[/] and will be available in a global `args` variable." + NewLine);

        return GetVersion() + NewLine + text;
    }

    /// <summary>
    /// Get assembly version for usage in --version
    /// </summary>
    private static FormattedString GetVersion()
    {
        var version = Assembly
            .GetExecutingAssembly()
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion ?? "unversioned";
        return FormattedStringParser.Parse($"[brightcyan bold]C# REPL {version}[/]");
    }

    /// <summary>
    /// In the help text, lists the available frameworks and marks one as default.
    /// </summary>
    private static string GetInstalledFrameworks(string leftPadding)
    {
        var frameworkList = SharedFramework
            .SupportedFrameworks
            .Select(fx => $"{leftPadding}- [cyan]{fx}[/]{(fx == Configuration.FrameworkDefault ? " [brightblack](default)[/]" : "")}");
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
                return $"{leftPadding}- [cyan]{themePath}[/]{(themePath == Configuration.DefaultThemeRelativePath ? " [brightblack](default)[/]" : "")}";
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

    private static void CreateDefaultConfigurationFile(string configFilePath)
    {
        try
        {
            File.WriteAllText(
                configFilePath,
                "# Add csharprepl command line options to this file to configure csharprepl." + NewLine +
                "# For example, uncomment the following line to configure tab size to 2:" + NewLine +
                "# --tabSize 2"
            );
        }
        catch (Exception ex) when (ex is IOException or SecurityException or UnauthorizedAccessException or DirectoryNotFoundException)
        {
            // If creating the default config file fails, don't consider that fatal, just warn and move on.
            Console.WriteLine("Warning, could not create default configuration file at path: " + configFilePath);
        }
    }

}
