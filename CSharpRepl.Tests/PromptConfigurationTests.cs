using CSharpRepl.PrettyPromptConfig;
using CSharpRepl.Services;
using CSharpRepl.Services.Roslyn;
using PrettyPrompt.Consoles;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using Xunit;

namespace CSharpRepl.Tests
{
    [Collection(nameof(RoslynServices))]
    public class PromptConfigurationTests : IAsyncLifetime
    {
        private readonly RoslynServices services;
        private readonly IConsole console;
        private readonly StringBuilder stdout;

        public PromptConfigurationTests()
        {
            var (console, stdout) = FakeConsole.CreateStubbedOutput();
            this.console = console;
            this.stdout = stdout;

            this.services = new RoslynServices(console, new Configuration(), new TestTraceLogger());
        }

        public Task InitializeAsync() => services.WarmUpAsync(Array.Empty<string>());
        public Task DisposeAsync() => Task.CompletedTask;

        [Theory]
        [MemberData(nameof(KeyPresses))]
        public void PromptConfiguration_CanCreate(object keyPress)
        {
            var configuration = PromptConfiguration.Configure(console, services);
            configuration.KeyPressCallbacks[keyPress].Invoke("Console.WriteLine(\"Hi!\");", 0);
        }

        public static IEnumerable<object[]> KeyPresses()
        {
            yield return new object[] { ConsoleKey.F1 };
            yield return new object[] { (ConsoleModifiers.Control, ConsoleKey.F1) };
            yield return new object[] { ConsoleKey.F9 };
            yield return new object[] { (ConsoleModifiers.Control, ConsoleKey.F9) };
        }
    }
}
