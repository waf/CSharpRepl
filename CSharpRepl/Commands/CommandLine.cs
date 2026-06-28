// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.CommandLine;
using System.CommandLine.Completions;
using System.CommandLine.Parsing;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using CSharpRepl.InjectedHook.Contracts;
using CSharpRepl.Services;
using CSharpRepl.Services.Completion;
using CSharpRepl.Services.Roslyn.References;
using CSharpRepl.Services.Theming;
using PrettyPrompt.Highlighting;
using Spectre.Console;
using Spectre.Console.Rendering;
using static System.Environment;

namespace CSharpRepl.Commands;

/// <summary>
/// Parses command line arguments using System.CommandLine.
/// Includes support for dotnet-suggest.
/// </summary>
internal static class CommandLine
{
    private const string DisableFurtherOptionParsing = "--";
    private const string EvalFileOptionName = "--eval-file";

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

    private static readonly Option<string?> Eval = new("--eval", "-e")
    {
        Description = "Evaluate C# code, print the result, and exit. Mutually exclusive with --eval-file."
    };

    private static readonly Option<string?> EvalFile = new("--eval-file")
    {
        Description = "Evaluate a .csx/.cs file, print the result, and exit. Mutually exclusive with --eval."
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

    private static readonly Option<string> AIProvider = new("--aiProvider")
    {
        Description = "AI provider preset (default: openai, use [green]--aiEndpoint[/] for other OpenAI-compatible APIs):"
    };

    private static readonly Option<string> AIApiKey = new("--aiApiKey")
    {
        Description = "API key for the AI provider. Alternatively, set its environment variable above."
    };

    private static readonly Option<string> AIEndpoint = new("--aiEndpoint")
    {
        Description = "Base URL of an OpenAI-compatible API. Overrides the selected provider's default endpoint."
    };

    private static readonly Option<string> AIModel = new("--aiModel")
    {
        Description = "Model to use for AI completions. Overrides the selected provider's default model."
    };

    private static readonly Option<string> AIPrompt = new("--aiPrompt")
    {
        Description = "System prompt to prefix to all code submissions"
    };

    private static readonly Option<int?> AIHistoryCount = new("--aiHistoryCount")
    {
        Description = "Number of previous REPL entries to send to the AI provider as context (default: 5)."
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

    private static readonly Option<string?> ConnectShell = BuildConnectShellOption();

    private static Option<string?> BuildConnectShellOption()
    {
        var option = new Option<string?>("--shell")
        {
            Description = "Shell syntax for the printed env vars: pwsh, powershell, cmd, bash, or fish. Auto-detected from the parent shell when omitted.",
        };
        // Offer the recognized shells for tab-completion. These aren't enforced: any other value falls back to
        // the pwsh-style default in BuildConnectInitExports, matching the lenient behavior elsewhere.
        option.CompletionSources.Add("pwsh", "powershell", "cmd", "bash", "fish");
        return option;
    }

    private static readonly Argument<int?> ConnectPid = new("pid")
    {
        Description = "Process id of the running, connector-enabled process to connect to.",
        Arity = ArgumentArity.ZeroOrOne,
    };

    private static readonly string ConnectUsage =
        "Usage: csharprepl connect <pid>                          connect to a running, connector-enabled process" + NewLine +
        "       csharprepl connect list                           list the connector-enabled processes you ca connect to" + NewLine +
        "       csharprepl connect init [--shell pwsh|bash|cmd]   print the env vars to launch your app with";

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
            StreamPipedInput, Eval, EvalFile, Trace, Version, Help, TabSize,
            AIProvider, AIApiKey, AIEndpoint, AIModel, AIPrompt, AIHistoryCount,
            TriggerCompletionListKeyBindings, NewLineKeyBindings, SubmitPromptKeyBindings, SubmitPromptDetailedKeyBindings,
            Configure, Culture,
        })
        {
            // Recursive so they also bind under the `connect <pid>` subcommand (e.g. `connect 1234 --theme ...`),
            // letting remote results render with the user's theme without redeclaring every option on `connect`.
            option.Recursive = true;
            availableCommands.Options.Add(option);
        }

        // The connect feature lives under the same parser as the REPL. `connect init [--shell ...]` prints the
        // env-var exports and exits; `connect <pid>` connects to a running, connector-enabled process and then
        // flows through the normal REPL configuration below (carrying the pid). The pid argument is optional so a
        // bare `connect` still parses — it's validated after the early-exit checks so we can show usage.
        var connectInit = new Command("init", "Print the environment variables to launch your app with so it can be connected.")
        {
            Options = { ConnectShell }
        };
        var connectList = new Command("list", "List the running, connector-enabled processes you ca connect to.");
        var connect = new Command("connect", "Connect to and evaluate code in a running, connector-enabled .NET process.")
        {
            Arguments = { ConnectPid },
            Subcommands = { connectInit, connectList },
        };
        availableCommands.Subcommands.Add(connect);

        // We drive everything through Parse (not Invoke), but System.CommandLine requires any command that has
        // subcommands to define an action; without one it adds a "Required command was not provided" error when
        // no subcommand is given (a plain REPL launch, or `connect <pid>`). These no-ops satisfy that contract.
        availableCommands.SetAction(_ => 0);
        connect.SetAction(_ => 0);

        var commandLine = availableCommands.Parse(parseArgs);
        var invokedCommand = commandLine.CommandResult.Command;

        // `connect init` is machine-consumable shell output: print the exports and exit before any REPL or
        // config-file setup (and before the config.rsp render options, which don't apply to it).
        if (invokedCommand == connectInit)
        {
            if (commandLine.Errors.Count > 0)
            {
                throw new InvalidOperationException(string.Join(NewLine, commandLine.Errors.Select(e => e.Message)));
            }
            // Wrapped in PlainText (not Text) so the exports are written verbatim — word-wrapping a long path
            // would corrupt a copy-paste or a pipe into the shell.
            return new Configuration(outputForEarlyExit: new PlainText(BuildConnectInitExports(ResolveInitShell(commandLine.GetValue(ConnectShell)))));
        }

        // `connect list` is a human-facing report of the currently attachable processes — print it and exit,
        // before any REPL or config-file setup (like `connect init`).
        if (invokedCommand == connectList)
        {
            if (commandLine.Errors.Count > 0)
            {
                throw new InvalidOperationException(string.Join(NewLine, commandLine.Errors.Select(e => e.Message)));
            }
            return new Configuration(outputForEarlyExit: BuildConnectListOutput());
        }

        if (!File.Exists(configFilePath))
        {
            ConfigurationFile.CreateDefaultConfigurationFile(configFilePath, availableCommands, ignoreCommands: new[] { Help, Version, Configure });
        }

        if (commandLine.GetValue(Configure))
        {
            ConfigurationFile.LaunchEditor(configFilePath);
            return new Configuration(outputForEarlyExit: new Text("Launching editor for " + configFilePath));
        }
        if (ShouldExitEarly(commandLine, configFilePath, out var text))
        {
            return new Configuration(outputForEarlyExit: text);
        }

        // `connect <pid>`: validate the pid (the argument is optional, so a bare `connect`, a non-numeric, or a
        // non-positive value reaches here) and surface one usage message for every bad form. We read the raw
        // token rather than GetValue(ConnectPid) because GetValue throws on an unparseable value (e.g. "abc").
        int? connectProcessId = null;
        if (invokedCommand == connect)
        {
            var pidToken = commandLine.GetResult(ConnectPid)?.Tokens is [var token] ? token.Value : null;
            if (!int.TryParse(pidToken, out var pid) || pid <= 0)
            {
                throw new InvalidOperationException(ConnectUsage);
            }
            connectProcessId = pid;
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
            evaluateInput: ResolveEvaluateInput(commandLine),
            connectProcessId: connectProcessId,
            tabSize: commandLine.GetValue(TabSize),
            trace: commandLine.GetValue(Trace),
            triggerCompletionListKeyPatterns: commandLine.GetValue(TriggerCompletionListKeyBindings),
            newLineKeyPatterns: commandLine.GetValue(NewLineKeyBindings),
            submitPromptKeyPatterns: commandLine.GetValue(SubmitPromptKeyBindings),
            submitPromptDetailedKeyPatterns: commandLine.GetValue(SubmitPromptDetailedKeyBindings),
            aiCompletionConfiguration: AICompleteService.CreateConfiguration(
                provider: commandLine.GetValue(AIProvider),
                apiKey: commandLine.GetValue(AIApiKey),
                endpoint: commandLine.GetValue(AIEndpoint),
                model: commandLine.GetValue(AIModel),
                prompt: commandLine.GetValue(AIPrompt),
                historyCount: commandLine.GetValue(AIHistoryCount)
            ),
            cultureName: commandLine.GetValue(Culture)
        );

        return config;
    }

    /// <summary>
    /// Resolves which shell syntax <c>connect init</c> should emit: 1. an explicit <c>--shell</c> (parsed by
    /// System.CommandLine into <paramref name="shell"/>) always wins; 2. otherwise detect the shell we were
    /// launched from; 3. else fall back to the OS default.
    /// </summary>
    private static string ResolveInitShell(string? shell)
    {
        if (!string.IsNullOrEmpty(shell)) return shell.ToLowerInvariant();
        return ShellDetector.DetectShell() ?? (OperatingSystem.IsWindows() ? "pwsh" : "bash");
    }

    /// <summary>
    /// Builds the shell-specific exports that activate the connector at the target's launch. The bootstrap DLL
    /// ships next to the tool under <c>connector/</c> (staged by packaging); its absolute path is what
    /// DOTNET_STARTUP_HOOKS needs so <c>StartupHook.Initialize()</c> runs before the target's Main.
    /// </summary>
    private static string BuildConnectInitExports(string shell)
    {
        const string HostingStartupAssembly = "CSharpRepl.InjectedHook";
        var bootstrap = Path.Combine(AppContext.BaseDirectory, "connector", "CSharpRepl.InjectedHook.dll");
        return shell switch
        {
            "cmd" => string.Join(NewLine,
                ":: Run in the shell that launches your app, then start it and note its process id:",
                ":: Do NOT set them as system-wide or user-wide environment variables; only set them in the shell.",
                $@"set ""DOTNET_STARTUP_HOOKS={bootstrap}""",
                $@"set ""ASPNETCORE_HOSTINGSTARTUPASSEMBLIES={HostingStartupAssembly}"""),
            "bash" or "sh" or "zsh" => string.Join(NewLine,
                "# Run in the shell that launches your app, then start it and note its process id:",
                "# Do NOT set them as system-wide or user-wide environment variables; only set them in the shell.",
                $@"export DOTNET_STARTUP_HOOKS=""{bootstrap}""",
                $@"export ASPNETCORE_HOSTINGSTARTUPASSEMBLIES=""{HostingStartupAssembly}"""),
            "fish" => string.Join(NewLine,
                "# Run in the shell that launches your app, then start it and note its process id:",
                "# Do NOT set them as system-wide or user-wide environment variables; only set them in the shell.",
                $@"set -gx DOTNET_STARTUP_HOOKS ""{bootstrap}""",
                $@"set -gx ASPNETCORE_HOSTINGSTARTUPASSEMBLIES ""{HostingStartupAssembly}"""),
            _ => string.Join(NewLine, // pwsh / powershell (default on Windows)
                "# Run in the shell that launches your app, then start it and note its process id:",
                "# Do NOT set them as system-wide or user-wide environment variables; only set them in the shell.",
                $@"$env:DOTNET_STARTUP_HOOKS = ""{bootstrap}""",
                $@"$env:ASPNETCORE_HOSTINGSTARTUPASSEMBLIES = ""{HostingStartupAssembly}"""),
        };
    }

    /// <summary>
    /// Builds the `connect list` report: the connector-enabled processes the current user ca connect to.
    /// The process name is read from the pid, which also drops a stale endpoint left behind by a crashed process.
    /// </summary>
    private static IRenderable BuildConnectListOutput()
    {
        var processes = ConnectorTransport.EnumerateListeningProcessIds()
            .Select(pid => (Pid: pid, Name: TryGetProcessName(pid)))
            .Where(p => p.Name is not null)
            .Select(p => (p.Pid, p.Name!))
            .ToList();

        return RenderConnectList(processes);
    }

    /// <summary>
    /// Renders the `connect list` report from an already-resolved (pid, name) set: a hint when nothing is
    /// attachable, otherwise a table plus the connect hint. Split from <see cref="BuildConnectListOutput"/>
    /// (which does the live discovery) so the rendering can be tested without depending on running processes.
    /// </summary>
    internal static IRenderable RenderConnectList(IReadOnlyList<(int ProcessId, string ProcessName)> processes)
    {
        if (processes.Count == 0)
        {
            return new Markup(
                "No connector-enabled processes found." + NewLine +
                "Launch your app with the environment variables from [green]csharprepl connect init[/], then run [green]csharprepl connect list[/] again.");
        }

        (bool containsDotNetExecutable, string? appExecutable, int pid) hint = (false, null, 0);

        var table = new Table()
            .MinimalBorder()
            .AddColumn("PID")
            .AddColumn("Process");
        // Use Text (not markup strings) for the cells so a process name containing '[' isn't parsed as markup.
        foreach (var (processId, processName) in processes)
        {
            table.AddRow(new Text(processId.ToString()), new Text(processName));

            hint.containsDotNetExecutable |= processName == "dotnet";
            if(processName != "dotnet" && hint.appExecutable is null)
            {
                hint.appExecutable = processName;
                hint.pid = processId;
            }
        }

        List<Renderable> rows = [
            table,
            new Markup("Connect with [green]csharprepl connect [/][cyan]<PID>[/]."),
        ];

        if(processes.Count == 2 && hint.containsDotNetExecutable && hint.appExecutable is not null)
        {
            rows.Add(new Markup($"Hint: you most likely want to connect to the '{hint.appExecutable}' process (PID {hint.pid})."));
        }

        return new Rows(rows);
    }

    /// <summary>Resolves a pid to its process name, or null if the process is gone (e.g. a stale endpoint).</summary>
    private static string? TryGetProcessName(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            return process.ProcessName;
        }
        catch
        {
            return null; // not running anymore — skip it
        }
    }

    private static bool ShouldExitEarly(ParseResult commandLine, string configFilePath, out IRenderable? text)
    {
        if (commandLine.Tokens.Any(token => token.Type == TokenType.Directive))
        {
            // this is just for dotnet-suggest directive processing. Invoking should write to stdout
            // and should not start the REPL. It's a feature of System.CommandLine.
            var output = new StringWriter();
            commandLine.Invoke(new InvocationConfiguration { Output = output });
            text = new Text(output.ToString()); // literal text — not markup
            return true;
        }
        if (commandLine.GetValue(Help))
        {
            text = new Markup(GetHelp(configFilePath));
            return true;
        }
        if (commandLine.GetValue(Version))
        {
            text = new Markup(GetVersion());
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

        for (int i = 0; i < args.Length; i++)
        {
            var arg = args[i];

            // Everything after "--" is forwarded to the load script as arguments (see
            // GetLoadScriptArgs), so stop handing tokens to the parser here.
            if (arg == DisableFurtherOptionParsing) yield break;

            // We allow csx files to be specified, sometimes in ambiguous scenarios that
            // System.CommandLine can't figure out. So we remove it from processing here,
            // and process it manually in ProcessScriptArguments. The value of --eval-file is the
            // exception: keep it so the option binds (it's read & run, not #loaded positionally).
            if (arg.EndsWith(".csx") && !IsEvalFileArgument(args, i))
            {
                continue;
            }

            yield return arg;
        }
    }

    /// <summary>
    /// True if <paramref name="args"/>[<paramref name="index"/>] is the path supplied to --eval-file,
    /// in either the "--eval-file path" or "--eval-file=path" form. Such a path must be excluded from
    /// the positional-.csx handling that otherwise treats a bare .csx as a load-script.
    /// </summary>
    private static bool IsEvalFileArgument(string[] args, int index)
    {
        var arg = args[index];
        if (arg.StartsWith(EvalFileOptionName + "=", StringComparison.Ordinal)) return true;
        return index > 0 && args[index - 1] == EvalFileOptionName;
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
    /// Resolves the C# to evaluate non-interactively from --eval (inline code) or --eval-file (file
    /// path). These are mutually exclusive. The resulting string is run and the process exits, unlike
    /// a positional .csx file which is #loaded and then drops into the interactive REPL.
    /// </summary>
    private static string? ResolveEvaluateInput(ParseResult commandLine)
    {
        var eval = commandLine.GetValue(Eval);
        var evalFile = commandLine.GetValue(EvalFile);

        if (eval is not null && evalFile is not null)
            throw new InvalidOperationException("Specify only one of --eval or --eval-file, not both.");

        if (eval is not null) return eval;

        if (evalFile is not null)
        {
            if (!File.Exists(evalFile)) throw new FileNotFoundException($@"Eval file ""{evalFile}"" was not found");
            return File.ReadAllText(evalFile);
        }

        return null;
    }

    /// <summary>
    /// Reads the contents of any provided script (csx) files.
    /// </summary>
    private static string? ProcessScriptArguments(string[] args)
    {
        var stringBuilder = new StringBuilder();
        for (int i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            if (arg == DisableFurtherOptionParsing) break;
            if (!arg.EndsWith(".csx")) continue;
            if (IsEvalFileArgument(args, i)) continue; // value of --eval-file: read & run by ResolveEvaluateInput, not #loaded
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
    private static string GetHelp(string configFilePath)
    {
        var text =
            "[underline]Usage[/]: [aqua]csharprepl[/] [green][[OPTIONS]][/] [cyan][[@response-file.rsp]][/] [cyan][[script-file.csx]][/] [green][[-- <additional-arguments>]][/]" + NewLine +
            "       [aqua]csharprepl[/] [green]connect[/] [cyan]<pid>[/]   or   [aqua]csharprepl[/] [green]connect list[/]   or   [aqua]csharprepl[/] [green]connect init[/] [green][[--shell <shell>]][/]" + NewLine + NewLine +
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
            $"  [green]-e[/] [cyan]<code>[/] or [green]--eval[/] [cyan]<code>[/]:                 {Eval.Description}" + NewLine +
            $"  [green]--eval-file[/] [cyan]<path>[/]:                         {EvalFile.Description}" + NewLine +
            $"  [green]--tabSize[/] [cyan]<width>[/]:                          {TabSize.Description}" + NewLine +
            $"  [green]--culture[/] [cyan]<culture name>[/]:                   {Culture.Description}" + NewLine +
            NewLine +
            $"  Key Bindings:                               {Configuration.KeyBindingPatternDescription}" + NewLine +
            $"                                              Specifying an option multiple times makes any of its key bindings trigger the action." + NewLine +
            $"  [green]--triggerCompletionListKeys[/] [cyan]<key-binding>[/]:  {TriggerCompletionListKeyBindings.Description}" + NewLine +
            $"  [green]--newLineKeys[/] [cyan]<key-binding>[/]:                {NewLineKeyBindings.Description}" + NewLine +
            $"  [green]--submitPromptKeys[/] [cyan]<key-binding>[/]:           {SubmitPromptKeyBindings.Description}" + NewLine +
            $"  [green]--submitPromptDetailedKeys[/] [cyan]<key-binding>[/]:   {SubmitPromptDetailedKeyBindings.Description}" + NewLine +
            NewLine +
            $"  AI Completions:" + NewLine +
            $"  [green]--aiProvider[/]:                               {AIProvider.Description}" + NewLine + GetAIProviderPresets(
            $"                                               ") + NewLine +
            $"  [green]--aiApiKey[/]:                                 {AIApiKey.Description}" + NewLine +
            $"  [green]--aiEndpoint[/]:                               {AIEndpoint.Description}" + NewLine +
            $"  [green]--aiModel[/]:                                  {AIModel.Description}" + NewLine +
            $"  [green]--aiPrompt[/]:                                 {AIPrompt.Description}" + NewLine +
            $"  [green]--aiHistoryCount[/]:                           {AIHistoryCount.Description}" + NewLine +
            NewLine +
            $"  Help and Diagnostics:" + NewLine +
            $"  [green]--trace[/]:                                    {Trace.Description}" + NewLine +
            $"  [green]-v[/] or [green]--version[/]:                            {Version.Description}" + NewLine +
            $"  [green]-h[/] or [green]--help[/]:                               {Help.Description}" + NewLine + NewLine +
            "[underline]COMMANDS[/]:" + NewLine +
            "  [green]connect[/] [cyan]<pid>[/]:" + NewLine +
            "      Connect to and evaluate code in a running, connector-enabled .NET process." + NewLine +
            "      The REPL [green][[OPTIONS]][/] above (e.g. [green]--theme[/]) also apply to the remote session." + NewLine +
            "  [green]connect list[/]:" + NewLine +
            "      List the running, connector-enabled processes you ca connect to (with their process ids)." + NewLine +
            "  [green]connect init[/] [green][[--shell pwsh|powershell|cmd|bash|fish]][/]:" + NewLine +
            "      Print the environment variables to launch your app with so it can be connected." + NewLine +
            "      The shell is auto-detected from the parent process when [green]--shell[/] is omitted." + NewLine + NewLine +
            "[cyan]@response-file.rsp[/]:" + NewLine +
            "  A file, with extension .rsp, containing the above command line [green][[OPTIONS]][/], one option per line." + NewLine +
            $"  Command line options will also be loaded from {configFilePath}" + NewLine +
            $"  Run 'csharprepl --configure' to launch this file in your editor." + NewLine + NewLine +
            "[cyan]script-file.csx[/]:" + NewLine +
            "  A file, with extension .csx, containing lines of C# to evaluate before starting the REPL." + NewLine +
            "  Arguments to this script can be passed as [green]<additional-arguments>[/] and will be available in a global `args` variable." + NewLine;

        return GetVersion() + NewLine + text;
    }

    /// <summary>
    /// Get assembly version for usage in --version
    /// </summary>
    private static string GetVersion()
    {
        var version = Assembly
            .GetExecutingAssembly()
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion ?? "unversioned";
        return $"[aqua bold]C# REPL {version}[/]";
    }

    /// <summary>
    /// In the help text, lists the available frameworks and marks one as default.
    /// </summary>
    private static string GetAIProviderPresets(string leftPadding)
    {
        var nameWidth = KnownAIProviders.All.Max(p => p.Name.Length);
        var presets = KnownAIProviders.All
            .Select(p => $"{leftPadding}- [cyan]{p.Name.PadRight(nameWidth)}[/]   [grey]({p.ApiKeyEnvironmentVariable})[/]");
        return string.Join(NewLine, presets);
    }

    private static string GetInstalledFrameworks(string leftPadding)
    {
        var frameworkList = SharedFramework
            .SupportedFrameworks
            .Select(fx => $"{leftPadding}- [cyan]{fx}[/]{(fx == Configuration.FrameworkDefault ? " [grey](default)[/]" : "")}");
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
                return $"{leftPadding}- [cyan]{themePath}[/]{(themePath == Configuration.DefaultThemeRelativePath ? " [grey](default)[/]" : "")}";
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
