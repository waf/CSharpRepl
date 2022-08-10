#nullable enable

using System;
using System.Threading.Tasks;
using CSharpRepl.PrettyPromptConfig;
using CSharpRepl.Services;
using CSharpRepl.Services.Roslyn;
using NSubstitute;
using PrettyPrompt;
using PrettyPrompt.Consoles;
using Xunit;

using static System.ConsoleKey;
using static System.ConsoleModifiers;

namespace CSharpRepl.Tests;

[Collection(nameof(RoslynServices))]
public partial class RoslynServicesTests : IAsyncLifetime
{
    private readonly RoslynServices services;

    public RoslynServicesTests()
    {
        var (console, _) = FakeConsole.CreateStubbedOutput();
        this.services = new RoslynServices(console, new Configuration(), new TestTraceLogger());
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
}

[Collection(nameof(RoslynServices))]
public class RoslynServices_REPL_Tests
{
    [Fact]
    public async Task CompleteStatement_DefaultKeyBindings()
    {
        var (console, repl, configuration) = await InitAsync();
        console.StubInput($"5 + 13{Enter}exit{Enter}");
        await repl.RunAsync(configuration);
        console.Received().WriteLine(Arg.Is<string>(str => str.Contains("18")));
    }

    [Fact]
    public async Task IncompleteStatement_DefaultKeyBindings()
    {
        var (console, repl, configuration) = await InitAsync();
        console.StubInput($"5 +{Enter}13{Enter}exit{Enter}");
        await repl.RunAsync(configuration);
        console.Received().WriteLine(Arg.Is<string>(str => str.Contains("18")));
    }

    [Fact]
    public async Task CompleteStatement_CustomKeyBindings()
    {
        var (console, repl, configuration) = await InitAsync(GetCustomKeyBindingsConfiguration());
        console.StubInput($"5 {Enter}+{Enter} 13{Control}{Enter}exit{Control}{Enter}");
        await repl.RunAsync(configuration);
        console.Received().WriteLine(Arg.Is<string>(str => str.Contains("18")));
    }

    [Fact]
    public async Task IncompleteStatement_CustomKeyBindings()
    {
        var (console, repl, configuration) = await InitAsync(GetCustomKeyBindingsConfiguration());
        console.StubInput($"5 +{Control}{Enter}exit{Control}{Enter}");
        await repl.RunAsync(configuration);
        console.Received().WriteErrorLine(Arg.Is<string>(str => str.Contains("Expected expression")));
    }

    [Fact]
    public async Task QuoteInsideCodeBlock_DoesNotOpenCompletionWindow()
    {
        // Typing the characters: { Console.WriteLine("Hello"); }
        // results in the string: { Console.WriteLine("Hello"as); }
        // because the completion window would open on the closing quote and commit on the closing parenthesis.
        // It happens quite often e.g. inside an if statement with parentheses.
        var (console, repl, configuration) = await InitAsync(GetCustomKeyBindingsConfiguration());
        console.StubInput($@"{{ Console.WriteLine(""Hello""); }}{Control}{Enter}exit{Control}{Enter}");
        await repl.RunAsync(configuration);
        console.DidNotReceive().WriteErrorLine(Arg.Any<string>());
    }

    [Fact]
    public async Task UsingStatement_CanBeCompleted()
    {
        var (console, repl, configuration) = await InitAsync();
        console.StubInput($@"using Syst{Tab};{Enter}exit{Enter}");
        await repl.RunAsync(configuration);
        console.DidNotReceive().WriteErrorLine(Arg.Any<string>());
    }

    /// <summary>
    /// https://github.com/waf/CSharpRepl/issues/145
    /// </summary>
    [Fact]
    public async Task LambdaArgs_CompletionDoesNotInterfere()
    {
        var (console, repl, configuration) = await InitAsync();
        console.StubInput($@""""".Where(c => c == 'x').Count(){Enter}{Enter}exit{Enter}");
        await repl.RunAsync(configuration);
        console.Received().WriteLine("0");
    }

    /// <summary>
    /// https://github.com/waf/CSharpRepl/issues/157
    /// </summary>
    [Fact]
    public async Task ObjectInitialization_CompletionDoesNotInterfere()
    {
        var (console, repl, configuration) = await InitAsync();
        console.StubInput($@"new {{ c = 5 }}.c{Enter}{Enter}exit{Enter}");
        await repl.RunAsync(configuration);
        console.Received().WriteLine("5");
    }

    private static async Task<(IConsole Console, ReadEvalPrintLoop Repl, Configuration Configuration)> InitAsync(Configuration? configuration = null)
    {
        var console = FakeConsole.Create();
        configuration ??= new Configuration();
        var services = new RoslynServices(console, configuration, new TestTraceLogger());
        var prompt = new Prompt(console: console, callbacks: new CSharpReplPromptCallbacks(console, services, configuration), configuration: new PromptConfiguration(keyBindings: configuration.KeyBindings));
        var repl = new ReadEvalPrintLoop(services, prompt, console);
        await services.WarmUpAsync(Array.Empty<string>());
        return (console, repl, configuration);
    }

    private static Configuration GetCustomKeyBindingsConfiguration()
    {
        return new Configuration(
            newLineKeyPatterns: new[] { "Enter" },
            submitPromptKeyPatterns: new[] { "Ctrl+Enter" });
    }
}