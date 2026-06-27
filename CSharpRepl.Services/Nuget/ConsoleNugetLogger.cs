// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis.Classification;
using NuGet.Common;
using Spectre.Console;

namespace CSharpRepl.Services.Nuget;

/// <summary>
/// Implementation of <see cref="ILogger"/> that renders NuGet restore output in the REPL.
/// When the console is interactive, a restore runs under an animated Spectre.Console status spinner with
/// log lines rendered above it.
///
/// When stdout is redirected (piped, <c>--eval</c>, captured by a tool) we fall back to plain, line-by-line text.
/// </summary>
internal sealed partial class ConsoleNugetLogger : ILogger
{
    // Segments worth highlighting within a line: 'quoted' text (group 1, usually a package id) and URLs
    // (group 2). Everything else is rendered in the line's body color.
    private static readonly Regex HighlightRegex = HighlightRegexGenerator();

    private const string LinkStyle = "blue";

    private readonly IConsoleService console;
    private readonly bool useUnicode;
    private readonly string errorPrefix;

    // The theme's string-literal color, as Spectre markup (e.g. "yellow"), used to highlight 'quoted'
    // package ids; empty when the theme defines no such color.
    private readonly string quoteStyle;

    private readonly bool interactive; // are we running in an interactive terminal, or is stdout redirected to a file/pipe?

    public ConsoleNugetLogger(IConsoleService console, Configuration configuration)
    {
        this.console = console;
        this.interactive = console.IsInteractive;
        this.useUnicode = configuration.UseUnicode;

        errorPrefix = useUnicode ? "❌ " : "";
        quoteStyle = configuration.Theme.GetSyntaxHighlightingSpectreColor(ClassificationTypeNames.StringLiteral)?.ToMarkup() ?? "";
    }

    /// <summary>
    /// Runs <paramref name="operation"/> under an animated status spinner labelled with the package being
    /// installed. Falls back to running the operation as-is (with plain, line-by-line output) when the
    /// console is non-interactive.
    /// </summary>
    public Task<T> WithStatusAsync<T>(string packageId, Func<Task<T>> operation)
    {
        if (!interactive)
        {
            return operation();
        }

        var name = quoteStyle.Length > 0 ? $"[{quoteStyle}]{Markup.Escape(packageId)}[/]" : Markup.Escape(packageId);
        var spinner = useUnicode ? Spinner.Known.Dots : Spinner.Known.Ascii;
        return console.RunWithStatusAsync($"Installing NuGet package {name}", spinner, "green", operation);
    }

    public void Log(ILogMessage message) => Log(message.Level, message.Message);

    public void Log(LogLevel level, string data)
    {
        switch (level)
        {
            case LogLevel.Information:
            case LogLevel.Minimal: LogProgress(data); return;
            case LogLevel.Warning: LogIssue(data, isError: false); return;
            case LogLevel.Error: LogIssue(data, isError: true); return;
            // Debug/Verbose are far too chatty for the REPL.
            default: return;
        }
    }

    public void LogDebug(string data) { }
    public void LogVerbose(string data) { }
    public void LogInformation(string data) => LogProgress(data);
    public void LogMinimal(string data) => LogProgress(data);
    public void LogInformationSummary(string data) => LogProgress(data);
    public void LogWarning(string data) => LogIssue(data, isError: false);
    public void LogError(string data) => LogIssue(data, isError: true);

    public Task LogAsync(LogLevel level, string data)
    {
        Log(level, data);
        return Task.CompletedTask;
    }

    public Task LogAsync(ILogMessage message)
    {
        Log(message);
        return Task.CompletedTask;
    }

    /// <summary>Writes the final outcome line of a restore. Called after the spinner has stopped.</summary>
    public void LogFinish(string text, bool success)
    {
        var data = Truncate(text);
        if (data.Length == 0) return;

        var prefix = success ? "" : errorPrefix;
        if (!interactive)
        {
            console.WriteLine();
            console.WriteStandardOutputLine(prefix + data);
            return;
        }

        console.WriteMarkupLine(Markup.Escape(prefix) + ToMarkup(data, success ? "green" : "red"));
    }

    // Information/Minimal/InformationSummary: progress detail, written as a persistent log line. While the
    // spinner is live Spectre renders these lines above it (they stay in the scrollback after it disappears).
    private void LogProgress(string data)
    {
        var text = Truncate(data);
        if (text.Length == 0) return;

        if (interactive)
        {
            console.WriteMarkupLine(ToMarkup(text, "white"));
        }
        else
        {
            console.WriteStandardOutputLine(text);
        }
    }

    // Warnings/errors are worth keeping in the scrollback, so they're written as persistent lines even
    // while the spinner is live (Spectre renders them above it).
    private void LogIssue(string data, bool isError)
    {
        var text = Truncate(data);
        if (text.Length == 0) return;

        if (!interactive)
        {
            console.WriteStandardOutputLine((isError ? errorPrefix : "") + text);
            return;
        }

        var prefix = isError ? errorPrefix : "";
        console.WriteMarkupLine(Markup.Escape(prefix) + ToMarkup(text, isError ? "red" : "yellow"));
    }

    private string ToMarkup(string text, string bodyStyle) => Highlight(text, bodyStyle, quoteStyle);

    /// <summary>
    /// Builds a Spectre markup string from raw NuGet text: escapes markup metacharacters (so a version
    /// range like "[1.0,2.0)" isn't parsed as markup) and highlights 'quoted' segments - usually package
    /// ids - with <paramref name="quoteStyle"/> (the theme's string-literal color), and URLs in blue.
    /// Everything else is rendered in <paramref name="bodyStyle"/>.
    /// </summary>
    internal static string Highlight(string text, string bodyStyle, string quoteStyle)
    {
        var sb = new StringBuilder();
        int pos = 0;
        foreach (Match match in HighlightRegex.Matches(text))
        {
            Append(text[pos..match.Index], bodyStyle);
            var isUrl = match.Groups[2].Success;
            var style = isUrl ? LinkStyle : (quoteStyle.Length > 0 ? quoteStyle : bodyStyle);
            Append(match.Value, style);
            pos = match.Index + match.Length;
        }
        Append(text[pos..], bodyStyle);
        return sb.ToString();

        void Append(string part, string style)
        {
            if (part.Length == 0) return;
            sb.Append('[').Append(style).Append(']').Append(Markup.Escape(part)).Append("[/]");
        }
    }

    /// <summary>
    /// NuGet output can be a bit overwhelming. Truncate some of the longer lines.
    /// </summary>
    private static string Truncate(string data)
    {
        if (string.IsNullOrWhiteSpace(data)) return "";

        if (data.StartsWith("Successfully installed", StringComparison.Ordinal))
            return ""; // ignore; NugetPackageInstaller will log success on its own

        if (data.IndexOf(" to folder", StringComparison.Ordinal) is int discard1 and >= 0)
            return data[..discard1];

        if (data.IndexOf(" with respect to project", StringComparison.Ordinal) is int discard2 and >= 0)
            return data[..discard2];

        if (data.IndexOf(" with content hash ", StringComparison.Ordinal) is int discard3 and >= 0)
            return data[..discard3];

        return data;
    }

    [GeneratedRegex(@"('.*?')|(https?://\S+)", RegexOptions.Compiled)]
    private static partial Regex HighlightRegexGenerator();
}
