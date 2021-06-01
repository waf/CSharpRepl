// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using CSharpRepl.Services;
using CSharpRepl.Services.Roslyn;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace CSharpRepl.Tests
{
    [Collection(nameof(RoslynServices))]
    public class CompletionTests : IAsyncLifetime
    {
        private readonly RoslynServices services;

        public CompletionTests()
        {
            var (console, _) = FakeConsole.CreateStubbedOutput();
            this.services = new RoslynServices(console, new Configuration());
        }

        public Task InitializeAsync() => services.WarmUpAsync();
        public Task DisposeAsync() => Task.CompletedTask;

        [Fact]
        public async Task Complete_GivenCode_ReturnsCompletions()
        {
            var completions = await this.services.Complete("Console.Writ", 12);
            var writelines = completions
                .Where(c => c.Item.DisplayText.StartsWith("Write"))
                .ToList();

            Assert.Equal("Write", writelines[0].Item.DisplayText);
            Assert.Equal("WriteLine", writelines[1].Item.DisplayText);

            var writeDescription = await writelines[0].DescriptionProvider.Value;
            Assert.Contains("Writes the text representation of the specified", writeDescription);
            var writeLineDescription = await writelines[1].DescriptionProvider.Value;
            Assert.Contains("Writes the current line terminator to the standard output", writeLineDescription);
        }

        /// <remarks>https://github.com/waf/CSharpRepl/issues/4</remarks>
        [Fact]
        public async Task Complete_SyntaxHighlight_CachesAreIsolated()
        {
            // type "c" which triggers completion at index 1, and is cached
            var completions = await this.services.Complete("c", 1);

            // next, type the number 1, which could collide with the previous cached value if the caches
            // aren't isolated, resulting in an exception
            var highlights = await this.services.SyntaxHighlightAsync("c1");

            Assert.NotEmpty(completions);
            Assert.NotEmpty(highlights);
        }
    }
}
