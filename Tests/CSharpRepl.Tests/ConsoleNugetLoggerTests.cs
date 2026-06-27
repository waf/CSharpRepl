// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Threading.Tasks;
using CSharpRepl.Services;
using CSharpRepl.Services.Nuget;
using NSubstitute;
using Spectre.Console;
using Xunit;

namespace CSharpRepl.Tests;

/// <summary>
/// Unit tests for <see cref="ConsoleNugetLogger"/>. These run headless: interactivity is taken from
/// <see cref="IConsoleService.IsInteractive"/> (faked here) rather than the process-global
/// <c>Console.IsOutputRedirected</c>, so both the interactive (spinner/markup) and redirected (plain text)
/// branches can be exercised in-process - no real terminal required.
/// <para>
/// The logger implements <c>NuGet.Common.ILogger</c>, so loading the type pulls in <c>NuGet.Common</c>,
/// which resolves from the SDK only after MSBuildLocator has run; <see cref="TestAssemblyInitializer"/>
/// arranges that for the whole assembly, so no <c>RoslynServices</c>-collection membership is needed here.
/// </para>
/// </summary>
public class ConsoleNugetLoggerTests
{
    private static ConsoleNugetLogger CreateLogger(bool interactive, bool useUnicode, out FakeConsoleAbstract console, out System.Text.StringBuilder stdout)
    {
        (console, stdout) = FakeConsole.CreateStubbedOutput();
        // Must be set before the logger reads it in its constructor.
        console.IsInteractive.Returns(interactive);
        return new ConsoleNugetLogger(console, new Configuration(useUnicode: useUnicode));
    }

    [Fact]
    public void Redirected_ProgressGoesToPlainStdout_NotAnsi()
    {
        var logger = CreateLogger(interactive: false, useUnicode: false, out var console, out var stdout);

        logger.LogInformation("  CACHE https://api.nuget.org/index.json");

        Assert.Contains("CACHE https://api.nuget.org/index.json", stdout.ToString());
        Assert.Equal("", console.AnsiConsole.Output);
    }

    [Fact]
    public void Interactive_ProgressGoesToAnsi_NotPlainStdout()
    {
        var logger = CreateLogger(interactive: true, useUnicode: false, out var console, out var stdout);

        logger.LogInformation("  CACHE https://api.nuget.org/index.json");

        var ansi = console.AnsiConsole.Output;
        Assert.Contains("CACHE", ansi);
        Assert.Contains("https://api.nuget.org/index.json", ansi);
        Assert.Equal("", stdout.ToString());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void DebugAndVerbose_AreSuppressed(bool interactive)
    {
        var logger = CreateLogger(interactive, useUnicode: false, out var console, out var stdout);

        logger.LogDebug("debug noise");
        logger.LogVerbose("verbose noise");

        Assert.Equal("", stdout.ToString());
        Assert.Equal("", console.AnsiConsole.Output);
    }

    [Fact]
    public void Interactive_Error_IncludesUnicodePrefix()
    {
        var logger = CreateLogger(interactive: true, useUnicode: true, out var console, out _);

        logger.LogError("could not restore package 'Foo'");

        var ansi = console.AnsiConsole.Output;
        Assert.Contains("❌", ansi);
        Assert.Contains("could not restore package 'Foo'", ansi);
    }

    [Fact]
    public void Progress_LongLinesAreTruncated()
    {
        var logger = CreateLogger(interactive: false, useUnicode: false, out _, out var stdout);

        logger.LogInformation("Installing Foo to folder C:\\packages\\foo");

        var output = stdout.ToString();
        Assert.Contains("Installing Foo", output);
        Assert.DoesNotContain("to folder", output);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task WithStatusAsync_RunsOperationAndReturnsResult(bool interactive)
    {
        var logger = CreateLogger(interactive, useUnicode: false, out _, out _);

        var ran = false;
        var result = await logger.WithStatusAsync("Humanizer", async () =>
        {
            ran = true;
            await Task.Yield();
            return 42;
        });

        Assert.True(ran);
        Assert.Equal(42, result);
    }

    [Fact]
    public void Highlight_RendersUrlsBlue()
    {
        var markup = ConsoleNugetLogger.Highlight("  CACHE https://api.nuget.org/index.json", bodyStyle: "white", quoteStyle: "yellow");

        Assert.Contains("[blue]https://api.nuget.org/index.json[/]", markup);
        Assert.Contains("[white]  CACHE [/]", markup);
    }

    [Fact]
    public void Highlight_RendersQuotedSegmentsWithQuoteStyle()
    {
        var markup = ConsoleNugetLogger.Highlight("Package 'Humanizer.3.0.10' installed", bodyStyle: "green", quoteStyle: "yellow");

        Assert.Contains("[yellow]'Humanizer.3.0.10'[/]", markup);
    }

    [Fact]
    public void Highlight_EscapesMarkupMetacharacters_SoVersionRangesDoNotBreakParsing()
    {
        var markup = ConsoleNugetLogger.Highlight("downgrade detected from [2.0.0] to [1.0.0]", bodyStyle: "white", quoteStyle: "yellow");

        Assert.Contains("[[", markup); // '[' is escaped to '[[' so it isn't parsed as a style tag
        _ = new Markup(markup);        // must be valid Spectre markup (constructor throws otherwise)
    }
}
