// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Text;
using System.Threading.Tasks;
using CSharpRepl.Services;
using CSharpRepl.Services.Roslyn;
using NSubstitute;
using PrettyPrompt;
using Xunit;

namespace CSharpRepl.Tests;

public sealed class RoslynServicesFixture : IAsyncLifetime
{
    public FakeConsoleAbstract ConsoleStub { get; }
    public StringBuilder CapturedConsoleOutput { get; }
    public StringBuilder CapturedConsoleError { get; }
    public IPrompt PromptStub { get; }
    public RoslynServices RoslynServices { get; }

    public RoslynServicesFixture()
    {
        (this.ConsoleStub, this.CapturedConsoleOutput, this.CapturedConsoleError) = FakeConsole.CreateStubbedOutputAndError();
        this.PromptStub = Substitute.For<IPrompt>();
        this.RoslynServices = new RoslynServices(ConsoleStub, new Configuration(), new TestTraceLogger());
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    public ValueTask InitializeAsync() => new(RoslynServices.WarmUpAsync([]));
}
