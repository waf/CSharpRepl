// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

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
    private readonly IConsoleService console;
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
        // pass a no-op browser launcher so invoking the F1/Ctrl+F1/F12 callbacks doesn't spawn a real browser.
        IPromptCallbacks configuration = new CSharpReplPromptCallbacks(console, services, new Configuration(), launchBrowser: _ => null);
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
    public async Task AiCompletionKeyBinding_NoApiKey_ReturnsEmptyStreamingResult()
    {
        IPromptCallbacks configuration = new CSharpReplPromptCallbacks(console, services, new Configuration());
        var ctrlAltSpace = new ConsoleKeyInfo(' ', ConsoleKey.Spacebar, shift: false, alt: true, control: true);
        Assert.True(configuration.TryGetKeyPressCallbacks(ctrlAltSpace, out var callback));

        // With no AI API key configured the completion stream yields nothing, but the callback
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

    [Fact]
    public async Task DecompileKeyBinding_InvalidCode_ReturnsErrorOutput()
    {
        IPromptCallbacks configuration = new CSharpReplPromptCallbacks(console, services, new Configuration());
        var f8 = new ConsoleKeyInfo('\0', ConsoleKey.F8, shift: false, alt: false, control: false);
        Assert.True(configuration.TryGetKeyPressCallbacks(f8, out var callback));

        var result = await callback.Invoke("this is not valid c# !@#$", 0, TestContext.Current.CancellationToken);

        // The decompiler fails to compile, so the error branch returns the red error message.
        Assert.NotNull(result);
        Assert.False(string.IsNullOrEmpty(result!.Output));
    }

    [Theory]
    [InlineData("exit")]
    [InlineData("clear")]
    [InlineData("help")]
    [InlineData("EXIT")]    // dispatch is case-insensitive, so the menu offers (and we decline committing) "exit" here too
    [InlineData("  exit  ")] // surrounding whitespace is trimmed before the command is dispatched
    public async Task ConfirmCompletionCommit_FullyTypedReplKeyword_DeclinesSoEnterSubmits(string text)
    {
        // Pressing Enter on a fully-typed REPL command must NOT commit the (identical) completion — otherwise the
        // Enter is swallowed and the user has to press it twice. Declining the commit lets the single Enter submit.
        IPromptCallbacks configuration = new CSharpReplPromptCallbacks(console, services, new Configuration());
        var enterKey = new KeyPress(new ConsoleKeyInfo('\0', ConsoleKey.Enter, shift: false, alt: false, control: false));

        var shouldCommit = await configuration.ConfirmCompletionCommit(text, text.Length, enterKey, CancellationToken.None);

        Assert.False(shouldCommit);
    }

    [Theory]
    [InlineData("exi")]      // partially typed: Enter should still complete it to "exit"
    [InlineData("Console")]  // ordinary code: not a REPL command
    public async Task ConfirmCompletionCommit_NonFullyTypedKeyword_CommitsAsUsual(string text)
    {
        IPromptCallbacks configuration = new CSharpReplPromptCallbacks(console, services, new Configuration());
        var enterKey = new KeyPress(new ConsoleKeyInfo('\0', ConsoleKey.Enter, shift: false, alt: false, control: false));

        var shouldCommit = await configuration.ConfirmCompletionCommit(text, text.Length, enterKey, CancellationToken.None);

        Assert.True(shouldCommit);
    }

    [Fact]
    public async Task ConfirmCompletionCommit_FullyTypedReplKeyword_NonSubmitKey_CommitsAsUsual()
    {
        // Tab isn't the submit key, so committing "exit" via Tab has no double-press downside — keep default behavior.
        IPromptCallbacks configuration = new CSharpReplPromptCallbacks(console, services, new Configuration());
        var tabKey = new KeyPress(new ConsoleKeyInfo('\t', ConsoleKey.Tab, shift: false, alt: false, control: false));

        var shouldCommit = await configuration.ConfirmCompletionCommit("exit", 4, tabKey, CancellationToken.None);

        Assert.True(shouldCommit);
    }

    public static IEnumerable<object[]> KeyPresses()
    {
        yield return new object[] { new ConsoleKeyInfo('\0', ConsoleKey.F1, shift: false, alt: false, control: false) };
        yield return new object[] { new ConsoleKeyInfo('\0', ConsoleKey.F1, shift: false, alt: false, control: true) };
        yield return new object[] { new ConsoleKeyInfo('\0', ConsoleKey.F8, shift: false, alt: false, control: false) };
        yield return new object[] { new ConsoleKeyInfo('\0', ConsoleKey.F8, shift: false, alt: false, control: true) };
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

    [Fact]
    public async Task TransformKeyPress_EnterOnUnterminatedDeclaration_Submits()
    {
        IPromptCallbacks configuration = new CSharpReplPromptCallbacks(console, services, new Configuration());
        var enterKey = new KeyPress(new ConsoleKeyInfo('\r', ConsoleKey.Enter, shift: false, alt: false, control: false));

        var transformed = await configuration.TransformKeyPressAsync("int i = 0", caret: 9, enterKey, CancellationToken.None);

        // submittable -> Enter passes through unchanged (it is not turned into an indented newline)
        Assert.Null(transformed.PastedText);
    }

    [Fact]
    public async Task TransformKeyPress_EnterOnIncompleteStatement_InsertsNewline()
    {
        IPromptCallbacks configuration = new CSharpReplPromptCallbacks(console, services, new Configuration());
        var enterKey = new KeyPress(new ConsoleKeyInfo('\r', ConsoleKey.Enter, shift: false, alt: false, control: false));

        var transformed = await configuration.TransformKeyPressAsync("if (x == 4)", caret: 11, enterKey, CancellationToken.None);

        Assert.Equal("\n", transformed.PastedText);
    }

    [Fact]
    public async Task FormatInput_SubmitOnUnterminatedDeclaration_InsertsSemicolon()
    {
        var callbacks = new TestableCallbacks(console, services, new Configuration());
        var enter = new KeyPress(new ConsoleKeyInfo('\r', ConsoleKey.Enter, shift: false, alt: false, control: false));

        var (text, caret) = await callbacks.Format("int i = 0", caret: 9, enter);

        Assert.Equal("int i = 0;", text);
        Assert.Equal(text.Length, caret);
    }

    [Theory]
    [InlineData("1 + 1")]        // already complete - no semicolon to add
    [InlineData("if (x == 4)")]  // not a single-line declaration the user merely didn't terminate
    public async Task FormatInput_SubmitOnNonAppendable_LeavesTextUnchanged(string code)
    {
        var callbacks = new TestableCallbacks(console, services, new Configuration());
        var enter = new KeyPress(new ConsoleKeyInfo('\r', ConsoleKey.Enter, shift: false, alt: false, control: false));

        var (text, _) = await callbacks.Format(code, caret: code.Length, enter);

        Assert.Equal(code, text);
    }

    /// <summary>Exposes the protected highlight and format callbacks for testing.</summary>
    private sealed class TestableCallbacks : CSharpReplPromptCallbacks
    {
        public TestableCallbacks(IConsoleService console, RoslynServices roslyn, Configuration configuration)
            : base(console, roslyn, configuration) { }

        public async Task<IReadOnlyCollection<FormatSpan>> Highlight(string text)
            => await HighlightCallbackAsync(text, default);

        public Task<(string Text, int Caret)> Format(string text, int caret, KeyPress key)
            => FormatInput(text, caret, key, default);
    }
}