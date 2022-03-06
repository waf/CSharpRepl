// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Threading.Tasks;
using CSharpRepl.Logging;
using CSharpRepl.PrettyPromptConfig;
using CSharpRepl.Services;
using CSharpRepl.Services.Logging;
using CSharpRepl.Services.Roslyn;
using PrettyPrompt;
using PrettyPrompt.Consoles;

namespace CSharpRepl;

/// <summary>
/// Main entry point; parses command line args and starts the <see cref="ReadEvalPrintLoop"/>.
/// Check out ARCHITECTURE.md in the root of the repo for some design documentation.
/// </summary>
internal static class Program
{
    internal static async Task<int> Main(string[] args)
    {
        var console = new SystemConsole();

        if (!TryParseArguments(args, out var config))
            return ExitCodes.ErrorParseArguments;

        if (config.OutputForEarlyExit is not null)
        {
            console.WriteLine(config.OutputForEarlyExit);
            return ExitCodes.Success;
        }

        var appStorage = CreateApplicationStorageDirectory();

        var logger = InitializeLogging(config.Trace);
        var roslyn = new RoslynServices(console, config, logger);
        var (prompt, exitCode) = InitializePrompt(console, appStorage, roslyn, config);

        if (prompt is not null)
        {
            await new ReadEvalPrintLoop(roslyn, prompt, console)
                .RunAsync(config)
                .ConfigureAwait(false);
        }

        return exitCode;
    }

    private static bool TryParseArguments(string[] args, [NotNullWhen(true)] out Configuration? configuration)
    {
        try
        {
            configuration = CommandLine.Parse(args);
            return true;
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.Error.WriteLine(ex.Message);
            Console.ResetColor();
            Console.WriteLine();
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

    /// <summary>
    /// Initialize logging. It's off by default, unless the user passes the --trace flag.
    /// </summary>
    private static ITraceLogger InitializeLogging(bool trace)
    {
        if (!trace)
        {
            return new NullLogger();
        }

        return TraceLogger.Create($"csharprepl-tracelog-{DateTime.UtcNow:yyyy-MM-dd}.txt");
    }

    private static (Prompt? prompt, int exitCode) InitializePrompt(SystemConsole console, string appStorage, RoslynServices roslyn, Configuration config)
    {
        try
        {
            var prompt = new Prompt(
               persistentHistoryFilepath: Path.Combine(appStorage, "prompt-history"),
               callbacks: new CSharpReplPromptCallbacks(console, roslyn, config),
               configuration: new PromptConfiguration(
                   keyBindings: config.KeyBindings,
                   prompt: config.Prompt));
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
}