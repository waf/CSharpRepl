#region License Header
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.
#endregion
using Sharply.Services;
using Sharply.Services.Roslyn;
using System.Threading.Tasks;
using Xunit;

namespace Sharply.Tests
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

        public Task InitializeAsync() => this.services.WarmUpAsync();
        public Task DisposeAsync() => Task.CompletedTask;

        [Fact]
        public async Task GetSymbolAtIndex_ReturnsFullyQualifiedName()
        {
            var symbol = await services.GetSymbolAtIndex(@"Console.WriteLine(""howdy"")", "Console.Wri".Length);
            Assert.Equal("System.Console.WriteLine", symbol.SymbolDisplay);
        }
    }
}
