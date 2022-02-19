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

namespace CSharpRepl;

/// <summary>
/// The core REPL; prints the welcome message, collects input with the <see cref="PrettyPrompt"/> library and
/// processes that input with <see cref="RoslynServices" />.
/// </summary>
internal sealed class ReadEvalPrintLoop
{
    private readonly RoslynServices roslyn;
    private readonly IPrompt prompt;
    private readonly IConsole console;

    public ReadEvalPrintLoop(RoslynServices roslyn, IPrompt prompt, IConsole console)
    {
        this.roslyn = roslyn;
        this.prompt = prompt;
        this.console = console;
    }

    private static readonly KeyPressPatterns submitWithDetailedOutputKeyPatterns = new(
        new KeyPressPattern(ConsoleModifiers.Control, ConsoleKey.Enter),
        new KeyPressPattern(ConsoleModifiers.Control | ConsoleModifiers.Alt, ConsoleKey.Enter));

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

                var displayDetails = submitWithDetailedOutputKeyPatterns.Matches(response.SubmitKeyInfo);
                await PrintAsync(roslyn, console, result, displayDetails);
            }
        }
    }

    private static async Task Preload(RoslynServices roslyn, IConsole console, Configuration config)
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

    private static async Task PrintAsync(RoslynServices roslyn, IConsole console, EvaluationResult result, bool displayDetails)
    {
        switch (result)
        {
            case EvaluationResult.Success ok:
                var formatted = await roslyn.PrettyPrintAsync(ok?.ReturnValue, displayDetails);
                console.WriteLine(formatted);
                break;
            case EvaluationResult.Error err:
                var formattedError = await roslyn.PrettyPrintAsync(err.Exception, displayDetails);
                console.WriteErrorLine(AnsiColor.Red.GetEscapeSequence() + formattedError + AnsiEscapeCodes.Reset);
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
        var highlightedKeyword = Color("preprocessor keyword") + keyword + AnsiEscapeCodes.Reset;
        var highlightedArgument = argument is null ? "" : Color("string") + @" """ + argument + @"""" + AnsiEscapeCodes.Reset;

        return highlightedKeyword + highlightedArgument;
    }

    private string Color(string reference) =>
        PromptConfiguration.HasUserOptedOutFromColor
        ? string.Empty
        : AnsiEscapeCodes.ToAnsiEscapeSequence(new ConsoleFormat(roslyn!.ToColor(reference)));

    private static string Underline(string word) =>
        AnsiEscapeCodes.ToAnsiEscapeSequence(new ConsoleFormat(Underline: true))
        + word + AnsiEscapeCodes.Reset;

    private string Help =>
        PromptConfiguration.HasUserOptedOutFromColor
        ? @"""help"""
        : AnsiColor.Green.GetEscapeSequence() + "help" + AnsiEscapeCodes.Reset;

    private string Exit =>
        PromptConfiguration.HasUserOptedOutFromColor
        ? @"""exit"""
        : AnsiColor.BrightRed.GetEscapeSequence() + "exit" + AnsiEscapeCodes.Reset;
}
