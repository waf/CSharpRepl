#nullable enable

using System;
using CSharpRepl.Services;
using CSharpRepl.Services.Roslyn.Formatting;
using CSharpRepl.Services.Roslyn.Formatting.CustomObjectFormatters;
using CSharpRepl.Services.SyntaxHighlighting;
using CSharpRepl.Services.Theming;
using Microsoft.Extensions.Caching.Memory;
using Xunit;

namespace CSharpRepl.Tests.ObjectFormatting;

internal class TestFormatter
{
    private readonly SyntaxHighlighter highlighter;
    private readonly Configuration config;

    public TestFormatter(SyntaxHighlighter highlighter, Configuration config)
    {
        this.highlighter = highlighter;
        this.config = config;
    }

    public string Format(ICustomObjectFormatter formatter, object value, Level level)
    {
        var prettyPrompt = new PrettyPrinter(highlighter, config);
        Assert.True(formatter.IsApplicable(value));
        return formatter.FormatToText(value, level, new Formatter(prettyPrompt, highlighter)).ToString();
    }

    public static TestFormatter Create() => new(
        new SyntaxHighlighter(
            new MemoryCache(new MemoryCacheOptions()),
            new Theme(null, null, null, null, Array.Empty<SyntaxHighlightingColor>())),
        new Configuration());
}