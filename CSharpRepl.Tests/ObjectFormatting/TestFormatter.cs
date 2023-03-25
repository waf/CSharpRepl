#nullable enable

using System;
using CSharpRepl.Services;
using CSharpRepl.Services.Roslyn.Formatting;
using CSharpRepl.Services.Roslyn.Formatting.CustomObjectFormatters;
using CSharpRepl.Services.SyntaxHighlighting;
using CSharpRepl.Services.Theming;
using Microsoft.Extensions.Caching.Memory;
using Spectre.Console;
using Xunit;

namespace CSharpRepl.Tests.ObjectFormatting;

internal class TestFormatter
{
    private readonly IAnsiConsole console;
    private readonly SyntaxHighlighter highlighter;
    private readonly Configuration config;

    public TestFormatter(IAnsiConsole console, SyntaxHighlighter highlighter, Configuration config)
    {
        this.console = console;
        this.highlighter = highlighter;
        this.config = config;
    }

    public string Format(ICustomObjectFormatter formatter, object value, Level level)
    {
        var prettyPrompt = new PrettyPrinter(console, highlighter, config);
        Assert.True(formatter.IsApplicable(value));
        return formatter.FormatToText(value, level, new Formatter(prettyPrompt, highlighter, console.Profile)).ToString();
    }

    public static TestFormatter Create(IAnsiConsole console) => new(
        console,
        new SyntaxHighlighter(
            new MemoryCache(new MemoryCacheOptions()),
            new Theme(null, null, null, null, Array.Empty<SyntaxHighlightingColor>())),
        new Configuration());
}