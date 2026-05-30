// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis.Classification;
using NuGet.Common;
using PrettyPrompt.Consoles;
using PrettyPrompt.Highlighting;

namespace CSharpRepl.Services.Nuget;

/// <summary>
/// Implementation of <see cref="ILogger"/> that logs minimal output to the console.
/// </summary>
internal sealed class ConsoleNugetLogger : ILogger
{
    private const int NumberOfMessagesToShow = 6;

    private readonly IConsoleEx console;
    private readonly Configuration configuration;
    private readonly string successPrefix;
    private readonly string errorPrefix;
    private readonly List<Line> lines = [];
    private readonly object linesLock = new(); // Lock for the lines list
    private int linesRendered;

    // The normal 'pretty' rendering uses cursor movement and the console buffer width, which only
    // work against a real terminal. When stdout is redirected (e.g. piped, --eval, or captured by a
    // tool) those operations throw "The handle is invalid", so if we're non-interactive we should
    // just write plain text.
    private readonly bool interactive;

    public ConsoleNugetLogger(IConsoleEx console, Configuration configuration)
    {
        this.console = console;
        this.configuration = configuration;
        this.interactive = !Console.IsOutputRedirected;

        successPrefix = configuration.UseUnicode ? "✅ " : "";
        errorPrefix = configuration.UseUnicode ? "❌ " : "";
    }

    public void Log(ILogMessage message) => Log(message.Level, message.Message);

    public void Log(LogLevel level, string data)
    {
        switch (level)
        {
            case LogLevel.Debug: LogDebug(data); return;
            case LogLevel.Verbose: LogVerbose(data); return;
            case LogLevel.Information: LogInformation(data); return;
            case LogLevel.Minimal: LogMinimal(data); return;
            case LogLevel.Warning: LogWarning(data); return;
            case LogLevel.Error: LogError(data); return;
            default: return;
        }
    }

    public void LogMinimal(string data)
    {
        var line = CreateLine(data, isError: false);
        if (line.IsEmpty) return;

        if (!interactive)
        {
            NonInteractiveAppendLine(line);
            return;
        }

        lock (linesLock)
        {
            lines.Add(line);
            if (lines.Count(l => !l.IsError) > NumberOfMessagesToShow)
            {
                for (int i = 0; i < lines.Count; i++)
                {
                    if (!lines[i].IsError)
                    {
                        lines.RemoveAt(i);
                        break;
                    }
                }
            }
        }

        RenderLines();
    }

    public void LogWarning(string data) => LogMinimal(data);
    public void LogInformationSummary(string data) => LogMinimal(data);

    public void LogError(string data)
    {
        var line = CreateLine(data, isError: true);

        if (!interactive)
        {
            NonInteractiveAppendLine(line);
            return;
        }

        lock (linesLock)
        {
            lines.Add(line);
        }
        RenderLines();
    }

    public void Reset()
    {
        lock (linesLock)
        {
            lines.Clear();
        }
        linesRendered = 0;
    }

    public void LogFinish(string text, bool success)
    {
        if (!interactive)
        {
            var summary = CreateLine(text, isError: !success);
            if (!summary.IsEmpty)
            {
                NonInteractiveAppendLine(summary);
            }

            return;
        }

        //delete rendered lines
        for (int i = 0; i < linesRendered; i++)
        {
            console.Write(AnsiEscapeCodes.GetMoveCursorUp(1));
            console.Write(AnsiEscapeCodes.ClearLine);
        }
        linesRendered = 0;

        lock (linesLock)
        {
            //keep only errors
            lines.RemoveAll(line => !line.IsError);
            //add final summary
            lines.Add(CreateLine(text, isError: !success));
        }

        //render summary + potential errors
        RenderLines();
    }

    // unused
    public Task LogAsync(LogLevel level, string data) => Task.CompletedTask;
    public Task LogAsync(ILogMessage message) => Task.CompletedTask;
    public void LogDebug(string data) { /* ignore, we don't need this much output */ }
    public void LogVerbose(string data) { /* ignore, we don't need this much output */ }
    public void LogInformation(string data) { /* ignore, we don't need this much output */ }

    private Line CreateLine(string data, bool isError) => new(data, isError, isError ? errorPrefix : successPrefix, configuration);

    /// <summary>
    /// Write the message as plain text. No cursor movement and no ANSI color.
    /// </summary>
    private void NonInteractiveAppendLine(Line line) => console.WriteStandardOutputLine(line.Text.Text ?? "");

    private void RenderLines()
    {
        try
        {
            console.Cursor.Show(false);
            for (int i = 0; i < linesRendered; i++)
            {
                console.Write(AnsiEscapeCodes.GetMoveCursorUp(1));
                console.Write(AnsiEscapeCodes.ClearLine);
            }

            lock (linesLock)
            {
                linesRendered = 0;
                foreach (var line in lines)
                {
                    if (line.IsError)
                    {
                        console.Write(AnsiColor.Red.GetEscapeSequence());
                        console.WriteLine(line.Text.Text ?? "");
                        console.Write(AnsiEscapeCodes.Reset);
                    }
                    else
                    {
                        console.WriteLine(line.Text);
                    }

                    linesRendered += Math.DivRem(line.Text.Length, console.BufferWidth, out var remainder) + (remainder == 0 ? 0 : 1);
                }
            }
        }
        finally
        {
            console.Cursor.Show(true);
        }
    }

    private readonly struct Line
    {
        private static readonly Regex QuotesRegex = new(@"'.*?'");

        public readonly FormattedString Text;
        public readonly bool IsError;
        public readonly bool IsEmpty;

        public Line(string data, bool isError, string prefix, Configuration configuration)
        {
            data = Truncate(data);
            IsEmpty = data.Length == 0;
            Text = IsEmpty ? prefix : Format(data, prefix, configuration);
            IsError = isError;
        }

        /// <summary>
        /// Nuget output can be a bit overwhelming. Truncate some of the longer lines
        /// </summary>
        private static string Truncate(string data)
        {
            if (string.IsNullOrWhiteSpace(data)) return "";

            if (data.IndexOf(" to folder") is int discard1 and >= 0)
                return data.Substring(0, discard1);

            if (data.IndexOf(" with respect to project") is int discard2 and >= 0)
                return data.Substring(0, discard2);

            if (data.StartsWith("Successfully installed"))
                return ""; //ignore; NugetPackageInstaller will log success on its own

            return data;
        }

        private static FormattedString Format(string text, string prefix, Configuration configuration)
        {
            text = prefix + text;
            if (configuration.Theme.TryGetSyntaxHighlightingAnsiColor(ClassificationTypeNames.StringLiteral, out var color))
            {
                var formattings = new List<FormatSpan>(1);
                foreach (Match match in QuotesRegex.Matches(text))
                {
                    formattings.Add(new FormatSpan(match.Index, match.Length, color));
                }
                return new FormattedString(text, formattings.ToArray());
            }

            return text;
        }
    }
}