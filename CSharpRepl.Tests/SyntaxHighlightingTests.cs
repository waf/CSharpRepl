// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Microsoft.CodeAnalysis.Text;
using CSharpRepl.Services;
using CSharpRepl.Services.Roslyn;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace CSharpRepl.Tests
{
    [Collection(nameof(RoslynServices))]
    public class SyntaxHighlightingTests : IAsyncLifetime
    {
        private readonly RoslynServices services;

        public SyntaxHighlightingTests()
        {
            var (console, _) = FakeConsole.CreateStubbedOutput();
            this.services = new RoslynServices(console, new Configuration
            {
                Theme = "Data/theme.json"
            });
        }

        public Task InitializeAsync() => this.services.WarmUpAsync();
        public Task DisposeAsync() => Task.CompletedTask;

        [Fact]
        public async Task SyntaxHighlightAsync_GivenCode_DetectsTextSpans()
        {
            var highlighted = await services.SyntaxHighlightAsync(@"var foo = ""bar"";");
            Assert.Equal(5, highlighted.Count);

            var expected = new TextSpan[] {
                new(0, 3), // var
                new(4, 3), // foo
                new(8, 1), // =
                new(10, 5),// "bar"
                new(15, 1) // ;
            };

            Assert.Equal(expected, highlighted.Select(highlight => highlight.TextSpan));
        }
    }
}
