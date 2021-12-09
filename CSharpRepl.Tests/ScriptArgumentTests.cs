using CSharpRepl.Services;
using CSharpRepl.Services.Roslyn;
using CSharpRepl.Services.Roslyn.Scripting;
using System;
using System.Threading.Tasks;
using Xunit;

namespace CSharpRepl.Tests;

[Collection(nameof(RoslynServices))]
public class ScriptArgumentTests
{
    [Theory]
    [InlineData("args[0]")] // array accessor
    [InlineData("Args[0]")] // IList<string> accessor
    public async Task Evaluate_WithArguments_ArgumentsAvailable(string argsAccessor)
    {
        var (console, _) = FakeConsole.CreateStubbedOutput();
        var services = new RoslynServices(console, new Configuration(), new TestTraceLogger());
        var args = new[] { "Howdy" };

        await services.WarmUpAsync(args);
        var variableAssignment = await services.EvaluateAsync($@"var x = {argsAccessor};");
        var variableUsage = await services.EvaluateAsync(@"x");

        Assert.IsType<EvaluationResult.Success>(variableAssignment);
        var usage = Assert.IsType<EvaluationResult.Success>(variableUsage);
        Assert.Equal("Howdy", usage.ReturnValue);
    }

    [Fact]
    public async Task Evaluate_PrettyPrint_PrintsPrettily()
    {
        var (console, stdOut) = FakeConsole.CreateStubbedOutput();
        var services = new RoslynServices(console, new Configuration(), new TestTraceLogger());

        await services.WarmUpAsync(Array.Empty<string>());
        _ = await services.EvaluateAsync("using System.Globalization;");
        _ = await services.EvaluateAsync("CultureInfo.DefaultThreadCurrentCulture = new System.Globalization.CultureInfo(\"en-US\");");
        var printStatement = await services.EvaluateAsync("Print(DateTime.MinValue)");

        Assert.IsType<EvaluationResult.Success>(printStatement);
        Assert.Equal("[1/1/0001 12:00:00 AM]" + Environment.NewLine, stdOut.ToString());
    }
}
