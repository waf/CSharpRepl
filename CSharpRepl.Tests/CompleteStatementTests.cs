using CSharpRepl.Services;
using CSharpRepl.Services.Roslyn;
using System;
using System.Threading.Tasks;
using Xunit;

namespace CSharpRepl.Tests
{
    [Collection(nameof(RoslynServices))]
    public class CompleteStatementTests : IAsyncLifetime
    {
        private readonly RoslynServices services;

        public CompleteStatementTests()
        {
            var (console, _) = FakeConsole.CreateStubbedOutput();
            this.services = new RoslynServices(console, new Configuration());
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
            bool isCompleteStatement = await services.IsTextCompleteStatement(code);
            Assert.Equal(shouldBeCompleteStatement, isCompleteStatement);
        }
    }
}
