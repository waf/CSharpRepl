// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.CommandLine;
using System.CommandLine.Completions;
using System.CommandLine.Parsing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using CSharpRepl.Services;
using CSharpRepl.Services.Completion;
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

    private static readonly Option<string[]?> References = new("--reference", "-r", "/r")
    {
        Description = "Reference assemblies, nuget packages, and csproj files. Can be specified multiple times.",
        AllowMultipleArgumentsPerToken = true
    };

    private static readonly Option<string[]?> Usings = BuildUsingsOption();

    private static readonly Option<string> Framework = BuildFrameworkOption();

    private static readonly Option<string> Theme = new("--theme", "-t", "/t")
    {
        Description = "Read a theme file for syntax highlighting. Respects the NO_COLOR standard.",
        DefaultValueFactory = _ => Configuration.DefaultThemeRelativePath
    };

    private static readonly Option<bool> UseTerminalPaletteTheme = new("--useTerminalPaletteTheme")
    {
        Description = "Uses terminal palette colors for syntax highlighting. Respects the NO_COLOR standard."
    };

    private static readonly Option<string> Prompt = new("--prompt")
    {
        Description = "Formatted prompt string.",
        DefaultValueFactory = _ => Configuration.PromptDefault
    };

    private static readonly Option<bool> UseUnicode = new("--useUnicode")
    {
        Description = "Use UTF8 output encoding and unicode character decorations (requires terminal support)."
    };

    private static readonly Option<bool> UsePrereleaseNugets = new("--usePrereleaseNugets")
    {
        Description = "Allows prerelease NuGet versions when searching for the latest package version."
    };

    private static readonly Option<bool> StreamPipedInput = new("--streamPipedInput")
    {
        Description = "If input is piped via stdin, evaluate it line by line instead of in one batch."
    };

    private static readonly Option<bool> Trace = new("--trace")
    {
        Description = "Produce a trace file in the current directory, for CSharpRepl bug reports."
    };

    private static readonly Option<bool> Version = new("--version", "-v", "/v")
    {
        Description = "Show version number and exit."
    };

    private static readonly Option<bool> Help = new("--help", "-h", "-?", "/h", "/?")
    {
        Description = "Show this help and exit."
    };

    private static readonly Option<int> TabSize = new("--tabSize")
    {
        Description = "Width of tab character.",
        DefaultValueFactory = _ => 4
    };

    private static readonly Option<string> OpenAIApiKey = new("--openAIApiKey")
    {
        Description = $"OpenAI API key. Alternatively, set the {OpenAICompleteService.ApiKeyEnvironmentVariableName} environment variable."
    };

    private static readonly Option<string> OpenAIPrompt = new("--openAIPrompt")
    {
        Description = "OpenAI prompt to prefix to all code submissions"
    };

    private static readonly Option<string> OpenAIModel = new("--openAIModel")
    {
        Description = "OpenAI model configuration"
    };

    private static readonly Option<int?> OpenAIHistoryCount = new("--openAIHistoryCount")
    {
        Description = "Maximum number of previous REPL entries to send to OpenAI as context."
    };

    private static readonly Option<string[]?> TriggerCompletionListKeyBindings = new("--triggerCompletionListKeys")
    {
        Description = "Key binding to trigger the completion list. Can be specified multiple times.",
        AllowMultipleArgumentsPerToken = true,
    };

    private static readonly Option<string[]?> NewLineKeyBindings = new("--newLineKeys")
    {
        Description = "Key binding to insert a newline character. Can be specified multiple times.",
        AllowMultipleArgumentsPerToken = true,
    };

    private static readonly Option<string[]?> SubmitPromptKeyBindings = new("--submitPromptKeys")
    {
        Description = "Key binding to submit the prompt. Can be specified multiple times.",
        AllowMultipleArgumentsPerToken = true,
    };

    private static readonly Option<string[]?> SubmitPromptDetailedKeyBindings = new("--submitPromptDetailedKeys")
    {
        Description = "Key binding to submit the prompt with detailed output. Can be specified multiple times.",
        AllowMultipleArgumentsPerToken = true,
    };

    private static readonly Option<bool> Configure = new("--configure")
    {
        Description = "Launches an editor to edit the CSharpRepl configuration file. Reads the EDITOR environment variable."
    };

    private static readonly Option<string> Culture = new("--culture")
    {
        Description = "Culture to use for access to the MSDN documentation. Defaults to the current culture."
    };

    private static Option<string[]?> BuildUsingsOption()
    {
        var option = new Option<string[]?>("--using", "-u", "/u")
        {
            Description = "Add using statement. Can be specified multiple times.",
            AllowMultipleArgumentsPerToken = true
        };
        option.CompletionSources.Add(GetAvailableUsings);
        return option;
    }

    private static Option<string> BuildFrameworkOption()
    {
        var option = new Option<string>("--framework", "-f", "/f")
        {
            Description = "Reference a shared framework.",
            DefaultValueFactory = _ => Configuration.FrameworkDefault
        };
        option.CompletionSources.Add(SharedFramework.SupportedFrameworks);
        option.Validators.Add(result =>
        {
            // when the option isn't specified on the command line its value comes from the
            // default value factory, which is always a supported framework, so skip validation.
            if (result.Implicit) return;

            string frameworkValue = result.GetValueOrDefault<string>() ?? string.Empty;
            if (!SharedFramework.SupportedFrameworks.Any(f => frameworkValue.StartsWith(f, StringComparison.OrdinalIgnoreCase)))
            {
                result.AddError("Unrecognized --framework value");
            }
        });
        return option;
    }

    public static Configuration Parse(string[] args, string configFilePath)
    {
        var parseArgs = PreProcessArguments(args, configFilePath).ToArray();

        var availableCommands = new RootCommand("C# REPL");

        // RootCommand adds built-in --help and --version options by default. We render our own
        // formatted help/version output, so remove the defaults to avoid duplicate-alias conflicts
        // with our Help and Version options below. The default dotnet-suggest directive added by
        // RootCommand is left in place (response files are also supported by default).
        availableCommands.Options.Clear();

        foreach (var option in new Option[]
        {
            References, Usings, Framework, Theme, UseTerminalPaletteTheme, Prompt, UseUnicode, UsePrereleaseNugets,
            StreamPipedInput, Trace, Version, Help, TabSize,
            OpenAIApiKey, OpenAIPrompt, OpenAIModel, OpenAIHistoryCount,
            TriggerCompletionListKeyBindings, NewLineKeyBindings, SubmitPromptKeyBindings, SubmitPromptDetailedKeyBindings,
            Configure, Culture,
        })
        {
            availableCommands.Options.Add(option);
        }

        var commandLine = availableCommands.Parse(parseArgs);

        if (!File.Exists(configFilePath))
        {
            ConfigurationFile.CreateDefaultConfigurationFile(configFilePath, availableCommands, ignoreCommands: new[] { Help, Version, Configure });
        }

        if (commandLine.GetValue(Configure))
        {
            ConfigurationFile.LaunchEditor(configFilePath);
            return new Configuration(outputForEarlyExit: "Launching editor for " + configFilePath);
        }
        if (ShouldExitEarly(commandLine, configFilePath, out var text))
        {
            return new Configuration(outputForEarlyExit: text);
        }
        if (commandLine.Errors.Count > 0)
        {
            throw new InvalidOperationException(string.Join(NewLine, commandLine.Errors.Select(e => e.Message)));
        }

        var config = new Configuration(
            references: commandLine.GetValue(References),
            usings: commandLine.GetValue(Usings),
            framework: commandLine.GetValue(Framework),
            loadScript: ProcessScriptArguments(args),
            loadScriptArgs: GetLoadScriptArgs(args),
            theme: commandLine.GetValue(Theme),
            useTerminalPaletteTheme: commandLine.GetValue(UseTerminalPaletteTheme),
            promptMarkup: commandLine.GetValue(Prompt) ?? Configuration.PromptDefault,
            useUnicode: commandLine.GetValue(UseUnicode),
            usePrereleaseNugets: commandLine.GetValue(UsePrereleaseNugets),
            streamPipedInput: commandLine.GetValue(StreamPipedInput),
            tabSize: commandLine.GetValue(TabSize),
            trace: commandLine.GetValue(Trace),
            triggerCompletionListKeyPatterns: commandLine.GetValue(TriggerCompletionListKeyBindings),
            newLineKeyPatterns: commandLine.GetValue(NewLineKeyBindings),
            submitPromptKeyPatterns: commandLine.GetValue(SubmitPromptKeyBindings),
            submitPromptDetailedKeyPatterns: commandLine.GetValue(SubmitPromptDetailedKeyBindings),
            openAIConfiguration: new OpenAIConfiguration(
                apiKey: commandLine.GetValue(OpenAIApiKey) ?? OpenAICompleteService.ApiKey,
                prompt: commandLine.GetValue(OpenAIPrompt) ?? OpenAICompleteService.DefaultPrompt,
                model: commandLine.GetValue(OpenAIModel) ?? OpenAICompleteService.DefaultModel,
                historyCount: commandLine.GetValue(OpenAIHistoryCount) ?? OpenAICompleteService.DefaultHistoryEntryCount
            ),
            cultureName: commandLine.GetValue(Culture)
        );

        return config;
    }

    private static bool ShouldExitEarly(ParseResult commandLine, string configFilePath, out FormattedString text)
    {
        if (commandLine.Tokens.Any(token => token.Type == TokenType.Directive))
        {
            // this is just for dotnet-suggest directive processing. Invoking should write to stdout
            // and should not start the REPL. It's a feature of System.CommandLine.
            var output = new StringWriter();
            commandLine.Invoke(new InvocationConfiguration { Output = output });
            text = output.ToString();
            return true;
        }
        if (commandLine.GetValue(Help))
        {
            text = GetHelp(configFilePath);
            return true;
        }
        if (commandLine.GetValue(Version))
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

        foreach (var arg in args)
        {
            // Everything after "--" is forwarded to the load script as arguments (see
            // GetLoadScriptArgs), so stop handing tokens to the parser here. System.CommandLine v3
            // no longer has a dedicated "unparsed tokens" bucket for these and would otherwise
            // report them as unrecognized arguments.
            if (arg == DisableFurtherOptionParsing) yield break;

            // We allow csx files to be specified, sometimes in ambiguous scenarios that
            // System.CommandLine can't figure out. So we remove it from processing here,
            // and process it manually in ProcessScriptArguments
            if (!arg.EndsWith(".csx"))
            {
                yield return arg;
            }
        }
    }

    /// <summary>
    /// Arguments after the "--" token are not parsed as options; they're forwarded to the
    /// load script and made available via a global `args` variable.
    /// </summary>
    private static string[] GetLoadScriptArgs(string[] args)
    {
        var doubleDashIndex = Array.IndexOf(args, DisableFurtherOptionParsing);
        return doubleDashIndex >= 0 ? args[(doubleDashIndex + 1)..] : [];
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

            //we are not loading content of the script manually because of https://github.com/waf/CSharpRepl/issues/140
            stringBuilder.AppendLine($"#load \"{arg}\"");
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
            $"  [green]--streamPipedInput[/]:                         {StreamPipedInput.Description}" + NewLine +
            $"  [green]--tabSize[/] [cyan]<width>[/]:                          {TabSize.Description}" + NewLine +
            $"  [green]--culture[/] [cyan]<culture name>[/]:                   {Culture.Description}" + NewLine +
            NewLine +
            $"  Key Bindings" + NewLine +
            $"  [green]--triggerCompletionListKeys[/] [cyan]<key-binding>[/]:  {TriggerCompletionListKeyBindings.Description}" + NewLine +
            $"  [green]--newLineKeys[/] [cyan]<key-binding>[/]:                {NewLineKeyBindings.Description}" + NewLine +
            $"  [green]--submitPromptKeys[/] [cyan]<key-binding>[/]:           {SubmitPromptKeyBindings.Description}" + NewLine +
            $"  [green]--submitPromptDetailedKeys[/] [cyan]<key-binding>[/]:   {SubmitPromptDetailedKeyBindings.Description}" + NewLine +
            NewLine +
            $"  Open AI" + NewLine +
            $"  [green]--openAIApiKey[/]:                             {OpenAIApiKey.Description}" + NewLine +
            $"  [green]--openAIPrompt[/]:                             {OpenAIPrompt.Description}" + NewLine +
            $"  [green]--openAIModel[/]:                              {OpenAIModel.Description}" + NewLine +
            $"  [green]--openAIHistoryCount[/]:                       {OpenAIHistoryCount.Description}" + NewLine +
            NewLine +
            $"  Help and Diagnostics" + NewLine +
            $"  [green]--trace[/]:                                    {Trace.Description}" + NewLine +
            $"  [green]-v[/] or [green]--version[/]:                            {Version.Description}" + NewLine +
            $"  [green]-h[/] or [green]--help[/]:                               {Help.Description}" + NewLine + NewLine +
            "[cyan]@response-file.rsp[/]:" + NewLine +
            "  A file, with extension .rsp, containing the above command line [green][[OPTIONS]][/], one option per line." + NewLine +
            $"  Command line options will also be loaded from {configFilePath}" + NewLine +
            $"  Run 'csharprepl --configure' to launch this file in your editor." + NewLine + NewLine +
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
    private static IEnumerable<string> GetAvailableUsings(CompletionContext context)
    {
        string wordToComplete = context.WordToComplete;

        if (string.IsNullOrEmpty(wordToComplete) || "Syste".StartsWith(wordToComplete, StringComparison.OrdinalIgnoreCase))
            return ["System"];

        if (!wordToComplete.StartsWith("System", StringComparison.OrdinalIgnoreCase))
            return [];

        var runtimeAssemblyPaths = Directory.GetFiles(RuntimeEnvironment.GetRuntimeDirectory(), "*.dll");
        using var mlc = new MetadataLoadContext(new PathAssemblyResolver(runtimeAssemblyPaths));

        var namespaces =
            from assembly in runtimeAssemblyPaths
            from type in GetTypes(assembly)
            where type.IsPublic
                  && type.Namespace is not null
                  && type.Namespace.StartsWith(wordToComplete, StringComparison.OrdinalIgnoreCase)
            select type.Namespace;

        return namespaces.Distinct().Take(16).ToArray();

        IEnumerable<Type> GetTypes(string assemblyPath)
        {
            try { return mlc.LoadFromAssemblyPath(assemblyPath).GetTypes(); }
            catch (BadImageFormatException) { return []; } // handle native DLLs that have no managed metadata.
        }
    }
}
