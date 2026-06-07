// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CSharpRepl.InjectedHook.Contracts;
using CSharpRepl.Logging;
using CSharpRepl.PrettyPromptConfig;
using CSharpRepl.Services;
using CSharpRepl.Services.Logging;
using CSharpRepl.Services.Remote;
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
            // Help/version/usage render word-wrapped; machine-consumable output (e.g. `inspect init` exports)
            // arrives as PlainText and is written verbatim. Either way, one branch.
            console.Write(earlyExit);
            console.WriteLine();
            return ExitCodes.Success;
        }

        // initialize roslyn
        var logger = InitializeLogging(config.Trace);

        // inspect mode: connect to the inspector in the target process and run the remote loop instead of
        // the local evaluation paths below. It builds its own RoslynServices, seeded with the target's
        // references + the inspector globals, so completion/highlighting are target-aware.
        if (config.InspectProcessId is { } inspectProcessId)
        {
            return await RunInspectModeAsync(systemConsole, console, appStorage, logger, config, inspectProcessId)
                .ConfigureAwait(false);
        }

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

    /// <summary>
    /// Connects to the inspector hosted in the target process and runs the remote REPL loop. The same
    /// interactive prompt as the local REPL is used; only evaluation and rendering are routed remotely.
    /// </summary>
    private static async Task<int> RunInspectModeAsync(
        ConsoleService? systemConsole, IConsoleService console, string appStorage, ITraceLogger logger, Configuration config, int processId)
    {
        RemoteSession session;
        try
        {
            console.WriteLine($"Connecting to the inspector in process {processId}...");
            session = await RemoteSession
                .ConnectAsync(processId, TimeSpan.FromSeconds(10), CancellationToken.None)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            console.WriteErrorLine($"Could not connect to the inspector in process {processId}: {ex.Message}");
            console.WriteErrorLine(
                "Launch the target with the inspector enabled first (run 'csharprepl inspect init' for the env vars), " +
                "and pass the managed runtime process id.");
            return ExitCodes.ErrorParseArguments;
        }

        await using (session)
        {
            // A self-contained single-file target has no on-disk assemblies (not even corlib), so the engine
            // can't build a metadata reference for anything and every evaluation would fail. Refuse up front
            // with a clear message rather than dropping the user into a prompt where nothing works.
            if (session.Handshake.AssemblyAvailability == TargetAssemblyAvailability.SelfContainedSingleFile)
            {
                console.WriteErrorLine(
                    "The target is a self-contained single-file app: its assemblies are bundled in memory with no " +
                    "on-disk path, so the inspector cannot compile against them and evaluation would always fail. " +
                    "Inspect a framework-dependent build (or a normal `dotnet App.dll` launch) instead.");
                return ExitCodes.ErrorParseArguments;
            }

            // Seed the controller-side editor services with the target's references + the inspector globals, so
            // completion and semantic highlighting see the target's own types and `services`/`Get<T>()`. Editor
            // services run here (not in the target), so there's no per-keystroke pipe hop.
            var remoteEditor = await BuildRemoteEditorContextAsync(session, console).ConfigureAwait(false);
            var roslyn = new RoslynServices(console, config, logger, remoteEditor);

            // The interactive prompt requires the real system console to drive the PrettyPrompt library.
            var (prompt, exitCode) = InitializePrompt(
                systemConsole ?? throw new InvalidOperationException("The interactive prompt requires the real system console."),
                appStorage, roslyn, config);
            if (prompt is null)
            {
                return exitCode;
            }

            try
            {
                await new RemoteReadEvalPrintLoop(console, session, roslyn, prompt)
                    .RunAsync(config)
                    .ConfigureAwait(false);
            }
            finally
            {
                await prompt.DisposeAsync().ConfigureAwait(false);
            }

            return ExitCodes.Success;
        }
    }

    /// <summary>
    /// Builds the remote editor seed by asking the inspector for the target's loaded-assembly paths. Editor
    /// services are advisory, so a failure (or a single-file target with no on-disk assemblies) degrades to a
    /// reference-less workspace — completion/highlighting then cover framework code and the inspector globals
    /// but not the target's own types — rather than failing the whole session.
    /// </summary>
    private static async Task<RemoteEditorContext> BuildRemoteEditorContextAsync(RemoteSession session, IConsoleService console)
    {
        try
        {
            var referencePaths = await session.GetReferencePathsAsync(CancellationToken.None).ConfigureAwait(false);
            return new RemoteEditorContext(referencePaths, typeof(InspectorGlobals));
        }
        catch (Exception ex)
        {
            console.WriteErrorLine($"Could not load the target's references for editor services; completion will be limited: {ex.Message}");
            return new RemoteEditorContext([], typeof(InspectorGlobals));
        }
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