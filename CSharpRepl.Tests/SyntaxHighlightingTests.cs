using Microsoft.CodeAnalysis.Text;
using Sharply.Services;
using Sharply.Services.Roslyn;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace Sharply.Tests
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
