// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using CSharpRepl.Logging;
using CSharpRepl.PrettyPromptConfig;
using CSharpRepl.Services;
using CSharpRepl.Services.Logging;
using CSharpRepl.Services.Roslyn;
using PrettyPrompt;

namespace CSharpRepl;

/// <summary>
/// Main entry point; parses command line args and starts the <see cref="ReadEvalPrintLoop"/>.
/// Check out ARCHITECTURE.md in the root of the repo for some design documentation.
/// </summary>
internal static class Program
{
    internal static Task<int> Main(string[] args) => RunAsync(args);

    /// <summary>
    /// Core entry point. The <paramref name="console"/> and <paramref name="inputRedirectedOverride"/>
    /// parameters are testing seams: production calls supply neither, so a real <see cref="ConsoleService"/>
    /// is constructed and the ambient <see cref="Console.IsInputRedirected"/> is used. Tests inject a fake
    /// console and an explicit redirected-input flag, which lets the piped-input path be exercised
    /// deterministically without reading from (or blocking on) the real standard input handle.
    /// </summary>
    internal static async Task<int> RunAsync(string[] args, IConsoleService? console = null, bool? inputRedirectedOverride = null)
    {
        // Tracked as the concrete type for the interactive-prompt path, which hands the raw
        // PrettyPromptConsole (protected on IConsoleEx) to the PrettyPrompt library.
        var systemConsole = console as ConsoleService;
        if (console is null)
        {
            // Only mutate the process-wide console encoding for real runs. Setting Console.InputEncoding
            // resets Console.In, which would discard any reader a test injected (e.g. via Console.SetIn).
            Console.InputEncoding = Encoding.UTF8;
            Console.OutputEncoding = Encoding.UTF8;
            systemConsole = new ConsoleService();
            console = systemConsole;
        }

        // parse command line input
        var appStorage = CreateApplicationStorageDirectory();
        var configFile = Path.Combine(appStorage, "config.rsp");

        if (!TryParseArguments(args, configFile, console, out var config))
            return ExitCodes.ErrorParseArguments;

        SetDefaultCulture(config);

        if (config.OutputForEarlyExit is { } earlyExit)
        {
            console.Write(earlyExit);
            console.WriteLine();
            return ExitCodes.Success;
        }

        // initialize roslyn
        var logger = InitializeLogging(config.Trace);
        var roslyn = new RoslynServices(console, config, logger);

        // --eval / --eval-file: evaluate the supplied code non-interactively and exit. Checked before
        // the stdin-redirected branch below so it works whether or not stdin is a TTY, and never blocks
        // trying to read empty redirected stdin.
        if (config.EvaluateInput is not null)
        {
            return await new PipedInputEvaluator(console, roslyn, config)
                .EvaluateStringAsync(config.EvaluateInput)
                .ConfigureAwait(false);
        }

        // we're getting piped input, just evaluate the input and exit.
        if (inputRedirectedOverride ?? Console.IsInputRedirected)
        {
            var evaluator = new PipedInputEvaluator(console, roslyn, config);
            return config.StreamPipedInput
                ? await evaluator.EvaluateStreamingPipeInputAsync().ConfigureAwait(false)
                : await evaluator.EvaluateCollectedPipeInputAsync().ConfigureAwait(false);
        }
        else if (config.StreamPipedInput)
        {
            console.WriteErrorLine("--streamPipedInput specified but no redirected input received. This configuration option should be used with redirected standard input.");
            return ExitCodes.ErrorParseArguments;
        }

        // we're being run interactively, start the prompt. This path is production-only — it needs the
        // real system console to drive the PrettyPrompt library.
        var (prompt, exitCode) = InitializePrompt(
            systemConsole ?? throw new InvalidOperationException("The interactive prompt requires the real system console."),
            appStorage, roslyn, config);
        if (prompt is not null)
        {
            try
            {
                await new ReadEvalPrintLoop(console, roslyn, prompt)
                    .RunAsync(config)
                    .ConfigureAwait(false);
            }
            finally
            {
                await prompt.DisposeAsync().ConfigureAwait(false);
            }
        }

        return exitCode;
    }

    private static bool TryParseArguments(string[] args, string configFilePath, IConsoleService console, [NotNullWhen(true)] out Configuration? configuration)
    {
        try
        {
            configuration = CommandLine.Parse(args, configFilePath);
            return true;
        }
        catch (Exception ex)
        {
            console.WriteStandardErrorLine(ex.Message);
            configuration = null;
            return false;
        }
    }

    /// <summary>
    /// Create application storage directory and return its path.
    /// This is where prompt history and nuget packages are stored.
    /// </summary>
    private static string CreateApplicationStorageDirectory()
    {
        var appStorage = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), ".csharprepl");
        Directory.CreateDirectory(appStorage);
        return appStorage;
    }

    private static void SetDefaultCulture(Configuration config)
    {
        CultureInfo.DefaultThreadCurrentUICulture = config.Culture;
        // theoretically we shouldn't need to do the following, but in practice we need it in order to
        // get compiler errors emitted by CSharpScript in the right language (see https://github.com/waf/CSharpRepl/issues/312)
        CultureInfo.DefaultThreadCurrentCulture = config.Culture;
    }

    /// <summary>
    /// Initialize logging. It's off by default, unless the user passes the --trace flag.
    /// </summary>
    private static ITraceLogger InitializeLogging(bool trace) =>
        !trace ? new NullLogger() : TraceLogger.Create($"csharprepl-tracelog-{DateTime.UtcNow:yyyy-MM-dd}.txt");

    private static (Prompt? prompt, int exitCode) InitializePrompt(ConsoleService console, string appStorage, RoslynServices roslyn, Configuration config)
    {
        try
        {
            var prompt = new Prompt(
               persistentHistoryFilepath: Path.Combine(appStorage, "prompt-history"),
               callbacks: new CSharpReplPromptCallbacks(console, roslyn, config),
               configuration: new PromptConfiguration(
                   keyBindings: config.KeyBindings,
                   prompt: config.Prompt,
                   completionBoxBorderFormat: config.Theme.GetCompletionBoxBorderFormat(),
                   completionItemDescriptionPaneBackground: config.Theme.GetCompletionItemDescriptionPaneBackground(),
                   selectedCompletionItemBackground: config.Theme.GetSelectedCompletionItemBackgroundColor(),
                   selectedTextBackground: config.Theme.GetSelectedTextBackground(),
                   tabSize: config.TabSize),
               console: console.PrettyPromptConsole);
            return (prompt, ExitCodes.Success);
        }
        catch (InvalidOperationException ex) when (ex.Message.EndsWith("error code: 87", StringComparison.Ordinal))
        {
            Console.Error.WriteLine(
                "Failed to initialize prompt. Please make sure that the current terminal supports ANSI escape sequences." + Environment.NewLine
                + (OperatingSystem.IsWindows()
                    ? @"This requires at least Windows 10 version 1511 (build number 10586) and ""Use legacy console"" to be disabled in the Command Prompt." + Environment.NewLine
                    : string.Empty)
            );
            return (null, ExitCodes.ErrorAnsiEscapeSequencesNotSupported);
        }
        catch (InvalidOperationException ex) when (ex.Message.EndsWith("error code: 6", StringComparison.Ordinal))
        {
            Console.Error.WriteLine("Failed to initialize prompt. Invalid output mode -- is output redirected?");
            return (null, ExitCodes.ErrorInvalidConsoleHandle);
        }
    }
}

internal static class ExitCodes
{
    public const int Success = 0;
    public const int ErrorParseArguments = 1;
    public const int ErrorAnsiEscapeSequencesNotSupported = 2;
    public const int ErrorInvalidConsoleHandle = 3;
    public const int ErrorCancelled = 3;
}