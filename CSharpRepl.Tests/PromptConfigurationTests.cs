using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CSharpRepl.PrettyPromptConfig;
using CSharpRepl.Services;
using CSharpRepl.Services.Roslyn;
using PrettyPrompt;
using PrettyPrompt.Consoles;
using PrettyPrompt.Highlighting;
using Xunit;

namespace CSharpRepl.Tests;

[Collection(nameof(RoslynServices))]
public class PromptConfigurationTests : IAsyncLifetime
{
    private readonly RoslynServices services;
    private readonly IConsoleEx console;
    private readonly StringBuilder stdout;

    public PromptConfigurationTests()
    {
        var (console, stdout) = FakeConsole.CreateStubbedOutput();
        this.console = console;
        this.stdout = stdout;

        this.services = new RoslynServices(console, new Configuration(), new TestTraceLogger());
    }

    public ValueTask InitializeAsync() => new(services.WarmUpAsync([]));
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Theory]
    [MemberData(nameof(KeyPresses))]
    public void PromptConfiguration_CanCreate(ConsoleKeyInfo keyInfo)
    {
        IPromptCallbacks configuration = new CSharpReplPromptCallbacks(console, services, new Configuration());
        Assert.True(configuration.TryGetKeyPressCallbacks(keyInfo, out var callback));
        callback.Invoke("Console.WriteLine(\"Hi!\");", 0, TestContext.Current.CancellationToken);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task PromptConfiguration_Identation(bool shiftPressed)
    {
        IPromptCallbacks configuration = new CSharpReplPromptCallbacks(console, services, new Configuration());
        var enterKey = new KeyPress(new ConsoleKeyInfo('\0', ConsoleKey.Enter, shift: shiftPressed, alt: false, control: false));

        var transformed = await configuration.TransformKeyPressAsync("if (true) {", 11, enterKey, CancellationToken.None);
        Assert.Equal("\n\t", transformed.PastedText);
    }

    /// <summary>
    /// The smart-indentation brace counter must ignore braces that appear inside comments, string
    /// literals and char literals when deciding how far to indent the auto-inserted newline.
    /// </summary>
    [Theory]
    [InlineData("if (true) { // }", "\n\t")]            // '}' inside a single-line comment is ignored
    [InlineData("if (true) { /* } */", "\n\t")]          // '}' inside a multi-line comment is ignored
    [InlineData("if (true) { var s = \"}\";", "\n\t")]   // '}' inside a string literal is ignored
    [InlineData("if (true) { var s = \"\\\"}\";", "\n\t")] // escaped quote then '}' inside a string is ignored
    [InlineData("if (true) { var c = '}';", "\n\t")]     // '}' inside a char literal is ignored
    [InlineData("if (true) { if (false) {", "\n\t\t")]   // nested open braces indent two levels
    public async Task SmartIndentation_IgnoresBracesInsideCommentsStringsAndChars(string text, string expectedPastedText)
    {
        IPromptCallbacks configuration = new CSharpReplPromptCallbacks(console, services, new Configuration());
        var enterKey = new KeyPress(new ConsoleKeyInfo('\0', ConsoleKey.Enter, shift: false, alt: false, control: false));

        var transformed = await configuration.TransformKeyPressAsync(text, text.Length, enterKey, CancellationToken.None);

        Assert.Equal(expectedPastedText, transformed.PastedText);
    }

    [Fact]
    public async Task OpenAiCompletionKeyBinding_NoApiKey_ReturnsEmptyStreamingResult()
    {
        IPromptCallbacks configuration = new CSharpReplPromptCallbacks(console, services, new Configuration());
        var ctrlAltSpace = new ConsoleKeyInfo(' ', ConsoleKey.Spacebar, shift: false, alt: true, control: true);
        Assert.True(configuration.TryGetKeyPressCallbacks(ctrlAltSpace, out var callback));

        // With no OpenAI API key configured the completion stream yields nothing, but the callback
        // still returns a (streaming) result rather than throwing.
        var result = await callback.Invoke("1 + ", 4, TestContext.Current.CancellationToken);

        Assert.NotNull(result);
    }

    [Fact]
    public async Task DisassembleKeyBinding_InvalidCode_ReturnsErrorOutput()
    {
        IPromptCallbacks configuration = new CSharpReplPromptCallbacks(console, services, new Configuration());
        var f9 = new ConsoleKeyInfo('\0', ConsoleKey.F9, shift: false, alt: false, control: false);
        Assert.True(configuration.TryGetKeyPressCallbacks(f9, out var callback));

        var result = await callback.Invoke("this is not valid c# !@#$", 0, TestContext.Current.CancellationToken);

        // The disassembler fails to compile, so the error branch returns the red error message.
        Assert.NotNull(result);
        Assert.False(string.IsNullOrEmpty(result!.Output));
    }

    public static IEnumerable<object[]> KeyPresses()
    {
        yield return new object[] { new ConsoleKeyInfo('\0', ConsoleKey.F1, shift: false, alt: false, control: false) };
        yield return new object[] { new ConsoleKeyInfo('\0', ConsoleKey.F1, shift: false, alt: false, control: true) };
        yield return new object[] { new ConsoleKeyInfo('\0', ConsoleKey.F9, shift: false, alt: false, control: false) };
        yield return new object[] { new ConsoleKeyInfo('\0', ConsoleKey.F9, shift: false, alt: false, control: true) };
        yield return new object[] { new ConsoleKeyInfo('\0', ConsoleKey.F12, shift: false, alt: false, control: false) };
        yield return new object[] { new ConsoleKeyInfo('\0', ConsoleKey.D, shift: false, alt: false, control: true) };
    }

    [Theory]
    [InlineData("help")]
    [InlineData("#help")]
    [InlineData("exit")]
    [InlineData("clear")]
    public async Task HighlightCallback_ReplKeyword_HighlightsTheWholeWord(string keyword)
    {
        var callbacks = new TestableCallbacks(console, services, new Configuration());

        var spans = await callbacks.Highlight(keyword);

        var span = Assert.Single(spans);
        Assert.Equal(0, span.Start);
        Assert.Equal(keyword.Length, span.Length);
    }

    [Fact]
    public async Task HighlightCallback_RegularCode_DelegatesToRoslynClassification()
    {
        var callbacks = new TestableCallbacks(console, services, new Configuration());

        // not a REPL keyword, so it must fall through to Roslyn syntax highlighting and produce
        // more than a single whole-line span (e.g. the keyword, the type and the identifier).
        var spans = await callbacks.Highlight("var x = 1;");

        Assert.NotEmpty(spans);
    }

    /// <summary>Exposes the protected highlight callback so the REPL-keyword highlighting can be tested.</summary>
    private sealed class TestableCallbacks : CSharpReplPromptCallbacks
    {
        public TestableCallbacks(IConsoleEx console, RoslynServices roslyn, Configuration configuration)
            : base(console, roslyn, configuration) { }

        public async Task<IReadOnlyCollection<FormatSpan>> Highlight(string text)
            => await HighlightCallbackAsync(text, default);
    }
}