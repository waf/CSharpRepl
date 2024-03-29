﻿// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CSharpRepl.PrettyPromptConfig;
using CSharpRepl.Services;
using CSharpRepl.Services.Extensions;
using CSharpRepl.Services.Roslyn;
using CSharpRepl.Services.Roslyn.Formatting;
using CSharpRepl.Services.Roslyn.Scripting;
using CSharpRepl.Services.Theming;
using PrettyPrompt;
using PrettyPrompt.Consoles;
using PrettyPrompt.Highlighting;
using Spectre.Console;

namespace CSharpRepl;

/// <summary>
/// The core REPL; prints the welcome message, collects input with the <see cref="PrettyPrompt"/> library and
/// processes that input with <see cref="RoslynServices" />.
/// </summary>
internal sealed class ReadEvalPrintLoop
{
    private readonly IConsoleEx console;
    private readonly RoslynServices roslyn;
    private readonly IPrompt prompt;

    public ReadEvalPrintLoop(IConsoleEx console, RoslynServices roslyn, IPrompt prompt)
    {
        this.console = console;
        this.roslyn = roslyn;
        this.prompt = prompt;
    }

    public async Task RunAsync(Configuration config)
    {
        console.WriteLine("Welcome to the C# REPL (Read Eval Print Loop)!");
        console.WriteLine("Type C# expressions and statements at the prompt and press Enter to evaluate them.");
        console.WriteLine($"Type {Help} to learn more, {Exit} to quit, and {Clear} to clear your terminal.");
        console.WriteLine(string.Empty);

        await Preload(roslyn, console, config).ConfigureAwait(false);

        while (true)
        {
            var response = await prompt.ReadLineAsync().ConfigureAwait(false);

            if (response is ExitApplicationKeyPress)
            {
                break;
            }

            if (response.IsSuccess)
            {
                var commandText = response.Text.Trim().ToLowerInvariant();

                // evaluate built in commands
                if (commandText == "exit") { break; }
                if (commandText == "clear") { console.Clear(); continue; }
                if (new[] { "help", "#help", "?" }.Contains(commandText))
                {
                    PrintHelp(config.KeyBindings, config.SubmitPromptDetailedKeys);
                    continue;
                }

                // evaluate results returned by special keybindings (configured in the PromptConfiguration.cs)
                if (response is KeyPressCallbackResult callbackOutput)
                {
                    console.WriteLine(Environment.NewLine + callbackOutput.Output);
                    continue;
                }

                response.CancellationToken.Register(() => Environment.Exit(1));

                // evaluate C# code and directives
                var result = await roslyn
                    .EvaluateAsync(response.Text, config.LoadScriptArgs, response.CancellationToken)
                    .ConfigureAwait(false);

                var displayDetails = config.SubmitPromptDetailedKeys.Matches(response.SubmitKeyInfo);
                await PrintAsync(roslyn, console, result, displayDetails ? Level.FirstDetailed : Level.FirstSimple);
            }
        }
    }

    private static async Task Preload(RoslynServices roslyn, IConsoleEx console, Configuration config)
    {
        bool hasReferences = config.References.Count > 0;
        bool hasLoadScript = config.LoadScript is not null;
        if (!hasReferences && !hasLoadScript)
        {
            _ = roslyn.WarmUpAsync(config.LoadScriptArgs); // don't await; we don't want to block the console while warmup happens.
            return;
        }

        if (hasReferences)
        {
            console.WriteLine("Adding supplied references...");
            var loadReferenceScript = string.Join("\r\n", config.References.Select(reference => $@"#r ""{reference}"""));
            var loadReferenceScriptResult = await roslyn.EvaluateAsync(loadReferenceScript).ConfigureAwait(false);
            await PrintAsync(roslyn, console, loadReferenceScriptResult, level: Level.FirstSimple).ConfigureAwait(false);
        }

        if (hasLoadScript)
        {
            console.WriteLine("Running supplied CSX file...");
            var loadScriptResult = await roslyn.EvaluateAsync(config.LoadScript!, config.LoadScriptArgs).ConfigureAwait(false);
            await PrintAsync(roslyn, console, loadScriptResult, level: Level.FirstSimple).ConfigureAwait(false);
        }
    }

    private static async Task PrintAsync(RoslynServices roslyn, IConsoleEx console, EvaluationResult result, Level level)
    {
        switch (result)
        {
            case EvaluationResult.Success ok:
                if (ok.ReturnValue.HasValue)
                {
                    var formatted = await roslyn.PrettyPrintAsync(ok.ReturnValue.Value, level);
                    console.Write(formatted);
                }
                console.WriteLine();
                break;
            case EvaluationResult.Error err:
                var formattedError = await roslyn.PrettyPrintAsync(err.Exception, level);

                var panel = new Panel(formattedError.ToParagraph())
                {
                    Header = new PanelHeader(err.Exception.GetType().Name, Justify.Center),
                    BorderStyle = new Style(foreground: Color.Red)
                };
                console.WriteError(panel, formattedError.ToString());
                console.WriteLine();
                break;
            case EvaluationResult.Cancelled:
                console.WriteErrorLine(
                    AnsiColor.Yellow.GetEscapeSequence() + "Operation cancelled." + AnsiEscapeCodes.Reset
                );
                break;
        }
    }


    private void PrintHelp(KeyBindings keyBindings, KeyPressPatterns submitPromptDetailedKeys)
    {
        var newLineBindingName = KeyPressPatternToString(keyBindings.NewLine.DefinedPatterns ?? []);
        var submitPromptName = KeyPressPatternToString((keyBindings.SubmitPrompt.DefinedPatterns ?? []).Except(submitPromptDetailedKeys.DefinedPatterns ?? []));
        var submitPromptDetailedName = KeyPressPatternToString(submitPromptDetailedKeys.DefinedPatterns ?? []);

        console.WriteLine(FormattedStringParser.Parse($"""
More details and screenshots are available at
[blue]https://github.com/waf/CSharpRepl/blob/main/README.md [/]

[underline]Evaluating Code[/]
Type C# code at the prompt and press:
  - {submitPromptName} to run it and get result printed,
  - {submitPromptDetailedName} to run it and get result printed with more details (member info, stack traces, etc.),
  - {newLineBindingName} to insert a newline (to support multiple lines of input).
If the code isn't a complete statement, pressing [green]Enter[/] will insert a newline.

[underline]Adding References[/]
Use the {Reference()} command to add reference to:
  - assembly ({Reference("AssemblyName")} or {Reference("path/to/assembly.dll")}),
  - NuGet package ({Reference("nuget: PackageName")} or {Reference("nuget: PackageName, version")}),
  - project ({Reference("path/to/my.csproj")} or {Reference("path/to/my.sln")}).

Use {Preprocessor("#load", "path-to-file")} to evaluate C# stored in files (e.g. csx files). This can
be useful, for example, to build a [{ToColor("string")}].profile.csx[/] that includes libraries you want
to load.

[underline]Exploring Code[/]
  - [green]{"F1"}[/]:  when the caret is in a type or member, open the corresponding MSDN documentation.
  - [green]{"F9"}[/]:  show the IL (intermediate language) for the current statement.
  - [green]{"F12"}[/]: open the type's source code in the browser, if the assembly supports Source Link.

[underline]Configuration Options[/]
All configuration, including theming, is done at startup via command line flags.
Run [green]--help[/] at the command line to view these options.
"""
        ));

        string Reference(string? argument = null) => Preprocessor("#r", argument);

        string Preprocessor(string keyword, string? argument = null)
        {
            var highlightedKeyword = $"[{ToColor("preprocessor keyword")}]{keyword}[/]";
            var highlightedArgument = argument is null ? "" : $" [{ToColor("string")}]\"{argument}\"[/]";
            return highlightedKeyword + highlightedArgument;
        }

        string ToColor(string classification) => roslyn!.ToColor(classification).ToString();

        static string KeyPressPatternToString(IEnumerable<KeyPressPattern> patterns)
        {
            var values = patterns.ToList();
            return values.Count > 0 ?
                string.Join(" or ", values.Select(pattern => $"[green]{pattern.GetStringValue()}[/]")) :
               "[red]<undefined>[/]";
        }
    }

    private static string Help =>
        PromptConfiguration.HasUserOptedOutFromColor
        ? @"""help"""
        : AnsiColor.Green.GetEscapeSequence() + "help" + AnsiEscapeCodes.Reset;

    private static string Exit =>
        PromptConfiguration.HasUserOptedOutFromColor
        ? @"""exit"""
        : AnsiColor.BrightRed.GetEscapeSequence() + "exit" + AnsiEscapeCodes.Reset;

    private static string Clear =>
        PromptConfiguration.HasUserOptedOutFromColor
        ? @"""clear"""
        : AnsiColor.BrightBlue.GetEscapeSequence() + "clear" + AnsiEscapeCodes.Reset;
}
