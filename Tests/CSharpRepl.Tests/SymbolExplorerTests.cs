// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Threading.Tasks;
using CSharpRepl.Services;
using CSharpRepl.Services.Roslyn;
using CSharpRepl.Services.SymbolExploration;
using Xunit;

namespace CSharpRepl.Tests;

[Collection(nameof(RoslynServices))]
public class SymbolExplorerTests : IAsyncLifetime
{
    private readonly RoslynServices services;

    public SymbolExplorerTests()
    {
        var (console, _) = FakeConsole.CreateStubbedOutput();
        this.services = new RoslynServices(console, new Configuration(), new TestTraceLogger());
    }

    public ValueTask InitializeAsync() => new(services.WarmUpAsync([]));
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task GetSymbolAtIndex_ReturnsFullyQualifiedName()
    {
        var symbol = await services.GetSymbolAtIndexAsync(@"Console.WriteLine(""howdy"")", "Console.Wri".Length);
        Assert.Equal("System.Console.WriteLine", symbol.SymbolDisplay);
    }

    [Fact]
    public async Task GetSymbolAtIndex_ClassInSourceLinkedAssembly_ReturnsSourceLinkUrl()
    {
        // should return a string like https://www.github.com/dotnet/dotnet/blob/b0f34d51fccc69fd334253924abd8d6853fad7aa/src/runtime/src/libraries/System.Console/src/System/Console.cs
        var symbol = await services.GetSymbolAtIndexAsync(@"Console.WriteLine(""howdy"")", "Conso".Length);

        Assert.StartsWith("https://www.github.com/dotnet/dotnet/", symbol.Url);
        Assert.EndsWith("Console.cs", symbol.Url);
    }

    [Fact]
    public async Task GetSymbolAtIndex_GenericTypeInSourceLinkedAssembly_ReturnsSourceLinkUrl()
    {
        // should return a string like https://www.github.com/dotnet/dotnet/blob/b0f34d51fccc69fd334253924abd8d6853fad7aa/src/runtime/src/libraries/System.Private.CoreLib/src/System/Collections/Generic/List.cs
        var symbol = await services.GetSymbolAtIndexAsync(@"List<string>", "Li".Length);

        Assert.StartsWith("https://www.github.com/dotnet/dotnet/", symbol.Url);
        Assert.EndsWith("List.cs", symbol.Url);
    }

    [Fact]
    public async Task GetSymbolAtIndex_MethodInSourceLinkedAssembly_ReturnsSourceLinkUrl()
    {
        // should return a string like https://www.github.com/dotnet/dotnet/blob/b0f34d51fccc69fd334253924abd8d6853fad7aa/src/runtime/src/libraries/System.Console/src/System/Console.cs#L733-L734
        var symbol = await services.GetSymbolAtIndexAsync(@"Console.WriteLine(""howdy"")", "Console.Wri".Length);

        AssertLinkWithLineNumber(symbol);
    }

    [Fact]
    public async Task GetSymbolAtIndex_PropertyInSourceLinkedAssembly_ReturnsSourceLinkUrl()
    {
        // should return a string like https://www.github.com/dotnet/dotnet/blob/b0f34d51fccc69fd334253924abd8d6853fad7aa/src/runtime/src/libraries/System.Console/src/System/Console.cs
        var symbol = await services.GetSymbolAtIndexAsync(@"Console.Out", "Console.Ou".Length);

        AssertLinkWithLineNumber(symbol);
    }

    [Fact]
    public async Task GetSymbolAtIndex_EventInSourceLinkedAssembly_ReturnsSourceLinkUrl()
    {
        // should return a string like https://www.github.com/dotnet/dotnet/blob/b0f34d51fccc69fd334253924abd8d6853fad7aa/src/runtime/src/libraries/System.Console/src/System/Console.cs
        var symbol = await services.GetSymbolAtIndexAsync(@"Console.CancelKeyPress", "Console.CancelKe".Length);

        AssertLinkWithLineNumber(symbol);
    }

    [Fact]
    public async Task GetSymbolAtIndex_CaretAtEndOfText_ReturnsSymbol()
    {
        // regression test for https://github.com/waf/CSharpRepl/issues/444
        // the caret is at the very end of the text, after the final "e" of "Console".
        const string text = "Console";
        var symbol = await services.GetSymbolAtIndexAsync(text, text.Length);
        Assert.Equal("System.Console", symbol.SymbolDisplay);
    }

    [Fact]
    public async Task GetSymbolAtIndex_CaretAtStartOfText_ReturnsSymbol()
    {
        // regression test for https://github.com/waf/CSharpRepl/issues/444
        // the caret is at the very start of the text, before the "C" of "Console".
        var symbol = await services.GetSymbolAtIndexAsync("Console", 0);
        Assert.Equal("System.Console", symbol.SymbolDisplay);
    }

    [Fact]
    public async Task GetSymbolAtIndex_CaretBetweenIdentifierAndDot_ReturnsClosestIdentifier()
    {
        // regression test for https://github.com/waf/CSharpRepl/issues/444
        // the caret sits on the boundary "Console|.WriteLine", so the closest identifier ("Console")
        // should win over the enclosing "Console.WriteLine" member access.
        var symbol = await services.GetSymbolAtIndexAsync(@"Console.WriteLine(""abc"");", "Console".Length);
        Assert.Equal("System.Console", symbol.SymbolDisplay);
    }

    [Fact]
    public async Task GetSymbolAtIndex_InvalidSymbol_NoException()
    {
        var symbol = await services.GetSymbolAtIndexAsync(@"wow!", 2);

        Assert.Equal(SymbolResult.Unknown, symbol);
    }

    [Fact]
    public async Task GetSymbolAtIndex_NonSourceLinkedAssembly_NoException()
    {
        _ = await services.EvaluateAsync(@"#r ""./Data/DemoLibrary.dll""", cancellationToken: TestContext.Current.CancellationToken);
        _ = await services.EvaluateAsync("using DemoLibrary;", cancellationToken: TestContext.Current.CancellationToken);
        var symbol = await services.GetSymbolAtIndexAsync("DemoClass.Multiply", "DemoClass.Multi".Length);

        Assert.Equal("DemoLibrary.DemoClass.Multiply", symbol.SymbolDisplay);
        Assert.Null(symbol.Url);
    }

    private static void AssertLinkWithLineNumber(SymbolResult symbol)
    {
        var urlParts = symbol.Url.Split('#');
        Assert.Equal(2, urlParts.Length);

        var url = urlParts[0];
        Assert.StartsWith("https://www.github.com/dotnet/dotnet/", url);

        var lineHash = urlParts[1];
        const string LinePattern = "L[0-9]+";
        Assert.Matches($"^{LinePattern}-{LinePattern}$", lineHash);
    }

}
