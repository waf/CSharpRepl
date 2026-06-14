// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

#nullable enable

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
        var prettyPrompt = new PrettyPrinter(console.Profile, highlighter, config);
        Assert.True(formatter.IsApplicable(value));
        return formatter.FormatToText(value, level, new Formatter(prettyPrompt, highlighter, console.Profile)).ToString();
    }

    public static TestFormatter Create(IAnsiConsole console) => new(
        console,
        new SyntaxHighlighter(
            new MemoryCache(new MemoryCacheOptions()),
            new Theme(null, null, null, null, [])),
        new Configuration());
}