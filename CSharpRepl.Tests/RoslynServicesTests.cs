#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CSharpRepl.PrettyPromptConfig;
using CSharpRepl.Services;
using CSharpRepl.Services.Roslyn;
using CSharpRepl.Services.Roslyn.Scripting;
using NSubstitute;
using PrettyPrompt;
using Xunit;

using static System.ConsoleKey;
using static System.ConsoleModifiers;

namespace CSharpRepl.Tests;

[Collection(nameof(RoslynServices))]
public partial class RoslynServicesTests : IAsyncLifetime, IClassFixture<RoslynServicesFixture>
{
    private readonly RoslynServices services;

    public RoslynServicesTests(RoslynServicesFixture fixture)
    {
        this.services = fixture.RoslynServices;
    }

    public Task InitializeAsync() => services.WarmUpAsync(Array.Empty<string>());
    public Task DisposeAsync() => Task.CompletedTask;

    [Theory]
    [InlineData("var x = 5;", true)]
    [InlineData("var x = ", false)]
    [InlineData("if (x == 4)", false)]
    [InlineData("if (x == 4) return;", true)]
    [InlineData("if you're happy and you know it, syntax error!", false)]
    public async Task IsCompleteStatement(string code, bool shouldBeCompleteStatement)
    {
        bool isCompleteStatement = await services.IsTextCompleteStatementAsync(code);
        Assert.Equal(shouldBeCompleteStatement, isCompleteStatement);
    }


    /// <summary>
    /// https://github.com/waf/CSharpRepl/issues/159
    /// </summary>
    [Theory]
    [InlineData("if(true){}", 10, "if (true) { }", 13, false)]
    [InlineData("if(true){}", 9, "if (true) { }", 11, false)]
    [InlineData("if(true){M  ( x  );}", 9, "if (true) { M(x); }", 11, false)]
    [InlineData("if(true){M  ( x  );}", 19, "if (true) { M(x); }", 17, false)]
    [InlineData("if(true){M  ( x  );}", 20, "if (true) { M(x); }", 19, false)]
    [InlineData("class C{", 8, "class C {", 9, false)]
    [InlineData("class C{int F;", 14, "class C { int F;", 16, false)]
    [InlineData("class C{int F;}", 15, "class C { int F; }", 18, false)]
    [InlineData("class C{\n\n}", 8, "class C\n{\n\n}", 9, false)]
    [InlineData("class C{\n\n}", 11, "class C\n{\n\n}", 12, false)]
    [InlineData("class C{\n\n    }", 15, "class C\n{\n\n}", 12, false)]

    [InlineData("Console.Write( 1+1 ); if(true  ){", 33, "Console.Write(1 + 1); if (true) {", 33, false)]
    [InlineData("Console.Write( 1+1 ); if(true  ){", 33, "Console.Write( 1+1 ); if (true) {", 33, true)]
    public async Task AutoFormat(string text, int caret, string expectedText, int expectedCaret, bool formatParentNodeOnly)
    {
        var (formattedText, formattedCaret) = await services.FormatInput(text, caret, formatParentNodeOnly, default);

        if (Environment.NewLine.Length == 2)
        {
            for (int i = 0; i < formattedCaret; i++)
            {
                if (formattedText[i] == '\r') ++expectedCaret;
            }
        }

        Assert.Equal(expectedText, formattedText.Replace("\r", ""));
        Assert.Equal(expectedCaret, formattedCaret);
    }

    [Theory]
    [InlineData("_ = 1", true, 1)]
    [InlineData("_ = 1;", false, null)]

    [InlineData("object o; o = null", true, null)]
    [InlineData("object o; o = null;", false, null)]

    [InlineData("int i = 1;", false, null)]

    [InlineData("\"abc\".ToString()", true, "abc")]
    [InlineData("\"abc\".ToString();", false, null)]

    [InlineData("object o = null; o?.ToString()", true, null)]
    [InlineData("object o = null; o?.ToString();", false, null)]

    [InlineData("Console.WriteLine()", false, null)]
    [InlineData("Console.WriteLine();", false, null)]
    public async Task NullOutput_Versus_NoOutput(string text, bool hasOutput, object? expectedOutput)
    {
        var result = (EvaluationResult.Success)await services.EvaluateAsync(text);

        Assert.Equal(hasOutput, result.ReturnValue.HasValue);

        if (hasOutput)
        {
            Assert.Equal(expectedOutput, result.ReturnValue.Value);
        }
        else
        {
            Assert.Null(expectedOutput);
        }
    }
}

[Collection(nameof(RoslynServices_REPL_Tests))]
public partial class RoslynServices_REPL_Tests : IAsyncLifetime, IClassFixture<RoslynServicesFixture>
{
    private readonly RoslynServices services;

    public RoslynServices_REPL_Tests(RoslynServicesFixture fixture)
    {
        this.services = fixture.RoslynServices;
    }

    public Task InitializeAsync() => services.WarmUpAsync(Array.Empty<string>());
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task CompleteStatement_DefaultKeyBindings()
    {
        var (console, repl, configuration, stdout, _) = await InitAsync();
        console.StubInput($"5 + 13{Enter}exit{Enter}");
        await repl.RunAsync(configuration);
        Assert.Contains("18", stdout.ToString());
    }

    [Fact]
    public async Task IncompleteStatement_DefaultKeyBindings()
    {
        var (console, repl, configuration, stdout, _) = await InitAsync();
        console.StubInput($"5 +{Enter}13{Enter}exit{Enter}");
        await repl.RunAsync(configuration);
        Assert.Contains("18", stdout.ToString());
    }

    [Fact]
    public async Task CompleteStatement_CustomKeyBindings()
    {
        var (console, repl, configuration, stdout, _) = await InitAsync(GetCustomKeyBindingsConfiguration());
        console.StubInput($"5 {Enter}+{Enter} 13{Control}{Enter}exit{Control}{Enter}");
        await repl.RunAsync(configuration);
        Assert.Contains("18", stdout.ToString());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task IncompleteStatement_CustomKeyBindings(bool isErrorRedirected)
    {
        var (console, repl, configuration, _, stderr) = await InitAsync(GetCustomKeyBindingsConfiguration());
        console.PrettyPromptConsole.IsErrorRedirected = isErrorRedirected;
        console.StubInput($"5 +{Control}{Enter}exit{Control}{Enter}");
        await repl.RunAsync(configuration);
        if (isErrorRedirected)
        {
            Assert.Contains("Expected expression", stderr.ToString());
        }
        else
        {
            Assert.Contains("Expected expression", console.AnsiConsole.Output);
        }
    }

    [Fact]
    public async Task QuoteInsideCodeBlock_DoesNotOpenCompletionWindow()
    {
        // Typing the characters: { Console.WriteLine("Hello"); }
        // results in the string: { Console.WriteLine("Hello"as); }
        // because the completion window would open on the closing quote and commit on the closing parenthesis.
        // It happens quite often e.g. inside an if statement with parentheses.
        var (console, repl, configuration, _, _) = await InitAsync(GetCustomKeyBindingsConfiguration());
        console.PrettyPromptConsole.IsErrorRedirected = true; // -> errors will go to PrettyPromptConsole
        console.StubInput($@"{{ Console.WriteLine(""Hello""); }}{Control}{Enter}exit{Control}{Enter}");
        await repl.RunAsync(configuration);
        console.PrettyPromptConsole.DidNotReceive().WriteErrorLine(Arg.Any<string>());
        console.PrettyPromptConsole.DidNotReceive().WriteError(Arg.Any<string>());
    }

    [Fact]
    public async Task UsingStatement_CanBeCompleted()
    {
        var (console, repl, configuration, _, _) = await InitAsync();
        console.PrettyPromptConsole.IsErrorRedirected = true; // -> errors will go to PrettyPromptConsole
        console.StubInput($@"using Syst{Tab};{Enter}exit{Enter}");
        await repl.RunAsync(configuration);
        console.PrettyPromptConsole.DidNotReceive().WriteErrorLine(Arg.Any<string>());
        console.PrettyPromptConsole.DidNotReceive().WriteError(Arg.Any<string>());
    }

    [Theory]
    [MemberData(nameof(EnumerateCompletionDoesNotInterfereData))]
    public async Task CompletionDoesNotInterfere(FormattableString input, string expectedInput, string expectedOutput)
    {
        var submittedInputs = new List<string>();
        var (console, repl, configuration, _, _) = await InitAsync();
        console.StubInput(input);
        services.EvaluatingInput += submittedInputs.Add;
        await repl.RunAsync(configuration);
        Assert.Equal(expectedInput, submittedInputs.Last());
        Assert.Equal(expectedOutput, console.AnsiConsole.Lines.Last());
    }

    public static IEnumerable<object[]> EnumerateCompletionDoesNotInterfereData()
    {
        foreach (var (input, expectedInput, expectedOutput) in EnumerateCompletionDoesNotInterfereData())
        {
            yield return new object[] { input, expectedInput, expectedOutput };
        }
        static IEnumerable<(FormattableString Input, string ExpectedInput, string ExpectedOutput)> EnumerateCompletionDoesNotInterfereData()
        {
            //https://github.com/waf/CSharpRepl/issues/145
            yield return
                (
                    $@""""".Where(c => c == 'x').Count(){Enter}exit{Enter}",
                    @""""".Where(c => c == 'x').Count()",
                    "0"
                );
            yield return
                (
                    $@""""".Where(c=>c=='x').Count(){Enter}exit{Enter}",
                    @""""".Where(c=>c=='x').Count()",
                    "0"
                );

            //https://github.com/waf/CSharpRepl/issues/157
            yield return
                (
                    $@"new {{ c = 5 }}.c{Enter}{Enter}exit{Enter}",
                    @"new { c = 5 }.c",
                    "5"
                );
            yield return
                (
                    $@"new{{c=5}}.c{Enter}{Enter}exit{Enter}",
                    @"new { c = 5 }.c",
                    "5"
                );

            //https://github.com/waf/CSharpRepl/issues/200
            yield return
                (
                    $@"new int[] {{ 1, 2, 3 }}.Select(){LeftArrow}i => i{RightArrow}.Count(){Enter}exit{Enter}",
                    @"new int[] { 1, 2, 3 }.Select(i => i).Count()",
                    "3"
                );
            yield return
                (
                    $@"new int[] {{1,2,3}}.Select(){LeftArrow}i=>i{RightArrow}.Count(){Enter}exit{Enter}",
                    @"new int[] { 1, 2, 3 }.Select(i=>i).Count()",
                    "3"
                );

            //https://github.com/waf/CSharpRepl/issues/201 - sequential writing
            yield return
                (
                    $@"new int[] {{ 1, 2, 3 }}.Select((i, v) => i + v).Count(){Enter}exit{Enter}",
                    @"new int[] { 1, 2, 3 }.Select((i, v) => i + v).Count()",
                    "3"
                );
            yield return
                (
                    $@"new int[] {{1,2,3}}.Select((i,v)=>i+v).Count(){Enter}exit{Enter}",
                    @"new int[] { 1, 2, 3 }.Select((i,v)=>i+v).Count()",
                    "3"
                );

            //https://github.com/waf/CSharpRepl/issues/201 - sequential writing and more than 2 lambda args
            yield return
                (
                    $@"new int[] {{ 1, 2, 3 }}.Select((i, v, {Backspace}{Backspace}) => i + v).Count(){Enter}exit{Enter}",
                    @"new int[] { 1, 2, 3 }.Select((i, v) => i + v).Count()",
                    "3"
                );
            yield return
                (
                    $@"new int[] {{1,2,3}}.Select((i,v,{Backspace})=>i+v).Count(){Enter}exit{Enter}",
                    @"new int[] { 1, 2, 3 }.Select((i,v)=>i+v).Count()",
                    "3"
                );

            //https://github.com/waf/CSharpRepl/issues/201 - editing completed expression (write 'Select()' then write the labmda inside)
            yield return
                (
                    $@"new int[] {{ 1, 2, 3 }}.Select(){LeftArrow}(i, v) => i + v{RightArrow}.Count(){Enter}exit{Enter}",
                    @"new int[] { 1, 2, 3 }.Select((i, v) => i + v).Count()",
                    "3"
                );
            yield return
                (
                    $@"new int[] {{1,2,3}}.Select(){LeftArrow}(i,v)=>i+v{RightArrow}.Count(){Enter}exit{Enter}",
                    @"new int[] { 1, 2, 3 }.Select((i,v)=>i+v).Count()",
                    "3"
                );

            //https://github.com/waf/CSharpRepl/issues/201 - editing completed expression (write 'Select((i, v) => i + v)' then delete 'i,' and write it again)
            yield return
                (
                    $@"new int[] {{ 1, 2, 3 }}.Select((i, v) => i + v){LeftArrow}{LeftArrow}{LeftArrow}{LeftArrow}{LeftArrow}{LeftArrow}{LeftArrow}{LeftArrow}{LeftArrow}{LeftArrow}{LeftArrow}{LeftArrow}{LeftArrow}{Backspace}{Backspace}i,{RightArrow}{RightArrow}{RightArrow}{RightArrow}{RightArrow}{RightArrow}{RightArrow}{RightArrow}{RightArrow}{RightArrow}{RightArrow}{RightArrow}{RightArrow}.Count(){Enter}exit{Enter}",
                    @"new int[] { 1, 2, 3 }.Select((i, v) => i + v).Count()",
                    "3"
                );

            //https://github.com/waf/CSharpRepl/issues/201 - editing completed expression (write 'Select((i, v) => i + v)' then delete 'v)' and write it again)
            yield return
                (
                    $@"new int[] {{ 1, 2, 3 }}.Select((i, v) => i + v){LeftArrow}{LeftArrow}{LeftArrow}{LeftArrow}{LeftArrow}{LeftArrow}{LeftArrow}{LeftArrow}{LeftArrow}{LeftArrow}{Backspace}{Backspace}v){RightArrow}{RightArrow}{RightArrow}{RightArrow}{RightArrow}{RightArrow}{RightArrow}{RightArrow}{RightArrow}{RightArrow}.Count(){Enter}exit{Enter}",
                    @"new int[] { 1, 2, 3 }.Select((i, v) => i + v).Count()",
                    "3"
                );

            //editing completed expression (write 'Select(i=>i)' then replace 'i' definition with '()' and then fill '()' with 'i,v')
            yield return
                (
                    $@"new int[] {{ 1, 2, 3 }}.Select(i=>i){LeftArrow}{LeftArrow}{LeftArrow}{LeftArrow}{Backspace}(){LeftArrow}i,v{RightArrow}{RightArrow}{RightArrow}{RightArrow}{RightArrow}.Count(){Enter}exit{Enter}",
                    @"new int[] { 1, 2, 3 }.Select((i,v)=>i).Count()",
                    "3"
                );
        }
    }

    private async Task<(FakeConsoleAbstract Console, ReadEvalPrintLoop Repl, Configuration Configuration, StringBuilder StdOut, StringBuilder StdErr)> InitAsync(Configuration? configuration = null)
    {
        var (console, stdout, stderr) = FakeConsole.CreateStubbedOutputAndError();
        configuration ??= new Configuration();

        var prompt = new Prompt(console: console.PrettyPromptConsole, callbacks: new CSharpReplPromptCallbacks(console, services, configuration), configuration: new PromptConfiguration(keyBindings: configuration.KeyBindings));
        var repl = new ReadEvalPrintLoop(console, services, prompt);
        await services.WarmUpAsync(Array.Empty<string>());
        return (console, repl, configuration, stdout, stderr);
    }

    private static Configuration GetCustomKeyBindingsConfiguration()
    {
        return new Configuration(
            newLineKeyPatterns: new[] { "Enter" },
            submitPromptKeyPatterns: new[] { "Ctrl+Enter" });
    }
}