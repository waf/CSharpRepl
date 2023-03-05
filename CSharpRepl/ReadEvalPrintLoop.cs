// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Linq;
using System.Threading.Tasks;
using CSharpRepl.PrettyPromptConfig;
using CSharpRepl.Services;
using CSharpRepl.Services.Roslyn;
using CSharpRepl.Services.Roslyn.Scripting;
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
    private readonly RoslynServices roslyn;
    private readonly IPrompt prompt;
    private readonly IConsoleEx console;

    public ReadEvalPrintLoop(RoslynServices roslyn, IPrompt prompt, IConsoleEx console)
    {
        this.roslyn = roslyn;
        this.prompt = prompt;
        this.console = console;
    }

    public async Task RunAsync(Configuration config)
    {
        console.WriteLine("Welcome to the C# REPL (Read Eval Print Loop)!");
        console.WriteLine("Type C# expressions and statements at the prompt and press Enter to evaluate them.");
        console.WriteLine($"Type {Help} to learn more, and type {Exit} to quit.");
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
                    PrintHelp();
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
                await PrintAsync(roslyn, console, result, displayDetails);
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
            await PrintAsync(roslyn, console, loadReferenceScriptResult, displayDetails: false).ConfigureAwait(false);
        }

        if (hasLoadScript)
        {
            console.WriteLine("Running supplied CSX file...");
            var loadScriptResult = await roslyn.EvaluateAsync(config.LoadScript!, config.LoadScriptArgs).ConfigureAwait(false);
            await PrintAsync(roslyn, console, loadScriptResult, displayDetails: false).ConfigureAwait(false);
        }
    }

    private static async Task PrintAsync(RoslynServices roslyn, IConsoleEx console, EvaluationResult result, bool displayDetails)
    {
        switch (result)
        {
            case EvaluationResult.Success ok:
                if (ok.ReturnValue.HasValue)
                {
                    var formatted = await roslyn.PrettyPrintAsync(ok.ReturnValue.Value, displayDetails);
                    console.Write(formatted);
                }
                console.WriteLine();
                break;
            case EvaluationResult.Error err:
                var formattedError = await roslyn.PrettyPrintAsync(err.Exception, displayDetails);

                var panel = new Panel(formattedError.ToParagraph())
                {
                    Header = new PanelHeader(" Exception ", Justify.Center),
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

    private void PrintHelp()
    {
        console.WriteLine(
$@"
More details and screenshots are available at
https://github.com/waf/CSharpRepl/blob/main/README.md

Evaluating Code
===============
Type C# at the prompt and press {Underline("Enter")} to run it. The result will be printed.
{Underline("Ctrl+Enter")} will also run the code, but show detailed member info / stack traces.
{Underline("Shift+Enter")} will insert a newline, to support multiple lines of input.
If the code isn't a complete statement, pressing Enter will insert a newline.

Adding References
=================
Use the {Reference()} command to add assembly or nuget references.
For assembly references, run {Reference("AssemblyName")} or {Reference("path/to/assembly.dll")}
For nuget packages, run {Reference("nuget: PackageName")} or {Reference("nuget: PackageName, version")}
For project references, run {Reference("path/to/my.csproj")} or {Reference("path/to/my.sln")} 

Use {Preprocessor("#load", "path-to-file")} to evaluate C# stored in files (e.g. csx files). This can
be useful, for example, to build a "".profile.csx"" that includes libraries you want
to load.

Exploring Code
==============
{Underline("F1")}: when the caret is in a type or member, open the corresponding MSDN documentation.
{Underline("F9")}: show the IL (intermediate language) for the current statement.
{Underline("F12")}: open the type's source code in the browser, if the assembly supports Source Link.

Configuration Options
=====================
All configuration, including theming, is done at startup via command line flags.
Run --help at the command line to view these options
"
        );
    }

    private string Reference(string? argument = null) =>
        Preprocessor("#r", argument);

    /// <summary>
    /// Produce syntax-highlighted strings like "#r reference" for the provided <paramref name="argument"/> string.
    /// </summary>
    private string Preprocessor(string keyword, string? argument = null)
    {
        var highlightedKeyword = Colorize("preprocessor keyword") + keyword + AnsiEscapeCodes.Reset;
        var highlightedArgument = argument is null ? "" : Colorize("string") + @" """ + argument + @"""" + AnsiEscapeCodes.Reset;

        return highlightedKeyword + highlightedArgument;
    }

    private string Colorize(string reference) =>
        PromptConfiguration.HasUserOptedOutFromColor
        ? string.Empty
        : AnsiEscapeCodes.ToAnsiEscapeSequenceSlow(new ConsoleFormat(roslyn!.ToColor(reference)));

    private static string Underline(string word) =>
        AnsiEscapeCodes.ToAnsiEscapeSequenceSlow(new ConsoleFormat(Underline: true))
        + word + AnsiEscapeCodes.Reset;

    private static string Help =>
        PromptConfiguration.HasUserOptedOutFromColor
        ? @"""help"""
        : AnsiColor.Green.GetEscapeSequence() + "help" + AnsiEscapeCodes.Reset;

    private static string Exit =>
        PromptConfiguration.HasUserOptedOutFromColor
        ? @"""exit"""
        : AnsiColor.BrightRed.GetEscapeSequence() + "exit" + AnsiEscapeCodes.Reset;
}
