using CSharpRepl.Services;
using CSharpRepl.Services.Roslyn;
using NSubstitute;
using PrettyPrompt;
using PrettyPrompt.Consoles;
using System;
using System.Threading.Tasks;
using Xunit;

namespace CSharpRepl.Tests
{
    public sealed class RoslynServicesFixture : IAsyncLifetime
    {
        public IConsole ConsoleStub { get; }
        public IPrompt PromptStub { get; }
        public RoslynServices RoslynServices { get; }

        public RoslynServicesFixture()
        {
            this.ConsoleStub = Substitute.For<IConsole>();
            this.PromptStub = Substitute.For<IPrompt>();
            this.RoslynServices = new RoslynServices(ConsoleStub, new Configuration(), new TestTraceLogger());
        }

        public Task DisposeAsync() => Task.CompletedTask;

        public Task InitializeAsync() => RoslynServices.WarmUpAsync(Array.Empty<string>());
    }
}