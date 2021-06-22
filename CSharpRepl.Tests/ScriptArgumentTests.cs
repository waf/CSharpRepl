using CSharpRepl.Services;
using CSharpRepl.Services.Roslyn;
using System.Threading.Tasks;
using Xunit;

namespace CSharpRepl.Tests
{
    [Collection(nameof(RoslynServices))]
    public class ScriptArgumentTests
    {
        [Fact]
        public async Task Evaluate_WithArguments_ArgumentsAvailable()
        {
            var (console, _) = FakeConsole.CreateStubbedOutput();
            var services = new RoslynServices(console, new Configuration());
            var args = new[] { "Howdy" };

            await services.WarmUpAsync(args);
            var variableAssignment = await services.Evaluate(@"var x = args[0];");
            var variableUsage = await services.Evaluate(@"x");

            Assert.IsType<EvaluationResult.Success>(variableAssignment);
            var usage = Assert.IsType<EvaluationResult.Success>(variableUsage);
            Assert.Equal("Howdy", usage.ReturnValue);
        }
    }
}
