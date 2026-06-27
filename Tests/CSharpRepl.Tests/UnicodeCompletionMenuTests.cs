// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Linq;
using System.Threading.Tasks;
using CSharpRepl.PrettyPromptConfig;
using CSharpRepl.Services;
using CSharpRepl.Services.Roslyn;
using Xunit;

namespace CSharpRepl.Tests;

/// <summary>
/// Exercises the autocomplete menu with <c>--useUnicode</c> turned on, end-to-end through the real
/// completion pipeline. Both kinds of menu entry pick up a leading kind-glyph, but via different
/// paths that must agree on the prefix layout: C# completions flow Roslyn → AutoCompleteService →
/// CompletionItemSymbols, while the help/exit/clear commands are glyphed in CSharpReplPromptCallbacks.
/// The bug-prone seam is offset alignment — if the glyph/padding length and the format-span offsets
/// disagree, the name's highlighting drifts — so we assert both the visible text and the spans.
/// </summary>
[Collection(nameof(RoslynServices))]
public sealed class UnicodeCompletionMenuTests : IAsyncLifetime
{
    private readonly RoslynServices services;
    private readonly CSharpReplPromptCallbacks promptCallbacks;

    public UnicodeCompletionMenuTests()
    {
        var (console, _) = FakeConsole.CreateStubbedOutput();
        var config = new Configuration(useUnicode: true);
        services = new RoslynServices(console, config, new TestTraceLogger());
        promptCallbacks = new CSharpReplPromptCallbacks(console, services, config);
    }

    public ValueTask InitializeAsync() => new(services.WarmUpAsync([]));
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task CompletionMenu_CSharpMethod_ShowsTintedGlyphWithAlignedName()
    {
        const string code = "Console.Writ";
        var completions = await promptCallbacks.GetCompletionItemsCoreAsync(code, code.Length, TestContext.Current.CancellationToken);

        var display = completions.First(c => c.ReplacementText == "WriteLine").DisplayTextFormatted;
        var spans = display.FormatSpans.ToArray();

        // glyph + two spaces + the original name, with nothing mangled in between.
        Assert.Equal("Ⓜ  WriteLine", display.Text);

        // The method glyph is tinted: a format span covers exactly the one-char glyph at the start.
        Assert.Contains(spans, s => s.Start == 0 && s.Length == 1);

        // The name's syntax highlighting begins exactly after the "Ⓜ  " prefix — i.e. the offset the
        // span builder used matches the prefix length the glyph builder produced.
        Assert.Contains(spans, s => s.Start == "Ⓜ  ".Length && s.Length == "WriteLine".Length);
    }

    [Fact]
    public async Task CompletionMenu_ReplCommand_ShowsUncoloredKeywordGlyph()
    {
        var completions = await promptCallbacks.GetCompletionItemsCoreAsync("he", 2, TestContext.Current.CancellationToken);

        var display = completions.First(c => c.ReplacementText == "help").DisplayTextFormatted;
        var spans = display.FormatSpans.ToArray();

        // help/exit/clear are treated as keywords: keyword glyph, then the command word.
        Assert.Equal("Ⓚ  help", display.Text);

        // The keyword glyph stays uncolored (no span at the glyph)...
        Assert.DoesNotContain(spans, s => s.Start == 0);

        // ...while the command word keeps its own color, offset past the "Ⓚ  " prefix.
        Assert.Contains(spans, s => s.Start == "Ⓚ  ".Length && s.Length == "help".Length);
    }
}
