// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using CSharpRepl.Services;
using CSharpRepl.Services.Roslyn;
using CSharpRepl.Services.SymbolExploration;
using System;
using System.Threading.Tasks;
using Xunit;

namespace CSharpRepl.Tests
{
    [Collection(nameof(RoslynServices))]
    public class SymbolExplorerTests : IAsyncLifetime
    {
        private readonly RoslynServices services;

        public SymbolExplorerTests()
        {
            var (console, _) = FakeConsole.CreateStubbedOutput();
            this.services = new RoslynServices(console, new Configuration());
        }

        public Task InitializeAsync() => services.WarmUpAsync(Array.Empty<string>());
        public Task DisposeAsync() => Task.CompletedTask;

        [Fact]
        public async Task GetSymbolAtIndex_ReturnsFullyQualifiedName()
        {
            var symbol = await services.GetSymbolAtIndexAsync(@"Console.WriteLine(""howdy"")", "Console.Wri".Length);
            Assert.Equal("System.Console.WriteLine", symbol.SymbolDisplay);
        }

        [Fact]
        public async Task GetSymbolAtIndex_ClassInSourceLinkedAssembly_ReturnsSourceLinkUrl()
        {
            // should return a string like https://www.github.com/dotnet/runtime/blob/208e377a5329ad6eb1db5e5fb9d4590fa50beadd/src/libraries/System.Console/src/System/Console.cs
            var symbol = await services.GetSymbolAtIndexAsync(@"Console.WriteLine(""howdy"")", "Conso".Length);

            Assert.StartsWith("https://www.github.com/dotnet/runtime/", symbol.Url);
            Assert.EndsWith("Console.cs", symbol.Url);
        }

        [Fact]
        public async Task GetSymbolAtIndex_MethodInSourceLinkedAssembly_ReturnsSourceLinkUrl()
        {
            // should return a string like https://www.github.com/dotnet/runtime/blob/208e377a5329ad6eb1db5e5fb9d4590fa50beadd/src/libraries/System.Console/src/System/Console.cs#L635-L636
            var symbol = await services.GetSymbolAtIndexAsync(@"Console.WriteLine(""howdy"")", "Console.Wri".Length);

            AssertLinkWithLineNumber(symbol);
        }

        [Fact]
        public async Task GetSymbolAtIndex_PropertyInSourceLinkedAssembly_ReturnsSourceLinkUrl()
        {
            // should return a string like https://www.github.com/dotnet/runtime/blob/208e377a5329ad6eb1db5e5fb9d4590fa50beadd/src/libraries/System.Console/src/System/Console.cs
            var symbol = await services.GetSymbolAtIndexAsync(@"Console.Out", "Console.Ou".Length);

            AssertLinkWithLineNumber(symbol);
        }

        [Fact]
        public async Task GetSymbolAtIndex_EventInSourceLinkedAssembly_ReturnsSourceLinkUrl()
        {
            // should return a string like https://www.github.com/dotnet/runtime/blob/208e377a5329ad6eb1db5e5fb9d4590fa50beadd/src/libraries/System.Console/src/System/Console.cs
            var symbol = await services.GetSymbolAtIndexAsync(@"Console.CancelKeyPress", "Console.CancelKe".Length);

            AssertLinkWithLineNumber(symbol);
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
            _ = await services.EvaluateAsync(@"#r ""./Data/DemoLibrary.dll""");
            _ = await services.EvaluateAsync("using DemoLibrary;");
            var symbol = await services.GetSymbolAtIndexAsync("DemoClass.Multiply", "DemoClass.Multi".Length);

            Assert.Equal("DemoLibrary.DemoClass.Multiply", symbol.SymbolDisplay);
            Assert.Null(symbol.Url);
        }

        private static void AssertLinkWithLineNumber(SymbolResult symbol)
        {
            var urlParts = symbol.Url.Split('#');
            Assert.Equal(2, urlParts.Length);

            var url = urlParts[0];
            Assert.StartsWith("https://www.github.com/dotnet/runtime/", url);

            var lineHash = urlParts[1];
            const string LinePattern = "L[0-9]+";
            Assert.Matches($"^{LinePattern}-{LinePattern}$", lineHash);
        }

    }
}
