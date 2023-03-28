#region License Header
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.
#endregion

using System;
using System.Net;
using System.Threading.Tasks;
using CSharpRepl.PrettyPromptConfig;
using CSharpRepl.Services;
using CSharpRepl.Services.Roslyn;
using PrettyPrompt;
using Xunit;
using static System.ConsoleKey;
using static System.ConsoleModifiers;

namespace CSharpRepl.Tests;

[Collection(nameof(RoslynServices))]
public class OpenAICompletionTests : IAsyncLifetime, IClassFixture<RoslynServicesFixture>
{
    private readonly FakeConsoleAbstract console;
    private readonly RoslynServices services;
    private readonly FakeHttp http;

    public OpenAICompletionTests(RoslynServicesFixture fixture)
    {
        var (console, _) = FakeConsole.CreateStubbedOutput();
        this.console = console;
        this.services = fixture.RoslynServices;
        this.http = new FakeHttp();
    }

    public Task InitializeAsync() => services.WarmUpAsync(Array.Empty<string>());
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Complete_GivenCode_ReturnsCompletions()
    {
        console.StubInput($@"Console.WriteLine({Control}{Alt}{Spacebar}{Enter}");

        this.http.Add(
            req => req.Contains("Console.WriteLine("),
            HttpStatusCode.OK,
            """
            data: {"choices": [{"text": "\"yay!\")"}], "id":"", "object": "", "created": 0, "model": ""}
            data: [DONE]
            """
        );

        var response = await GetPromptWithOpenAIModel("text-davinci-003").ReadLineAsync();

        Assert.Equal("""Console.WriteLine("yay!")""", response.Text);
    }

    [Fact]
    public async Task ChatComplete_GivenCode_ReturnsCompletions()
    {
        console.StubInput($@"// if you're happy and you know it clap your hands{Control}{Alt}{Spacebar}{Enter}");

        this.http.Add(
            req => req.Contains("clap your hands"),
            HttpStatusCode.OK,
            """
            data: {"choices": [{"delta": {"role": "assistant"}}], "id":"" }
            data: {"choices": [{"delta": {"content": "// claps hands"}}], "id":"" }
            data: [DONE]
            """
        );

        var response = await GetPromptWithOpenAIModel("gpt3.5-turbo").ReadLineAsync();

        Assert.Equal("// if you're happy and you know it clap your hands" + Environment.NewLine + "// claps hands", response.Text);
    }

    [Theory]
    [InlineData("text-davinci-003")]
    [InlineData("gpt3.5-turbo")]
    public async Task Complete_ExternalError_DoesNotCrash(string model)
    {
        console.StubInput($@"Console.WriteLine({Control}{Alt}{Spacebar}{Enter}");

        this.http.Add(
            req => req.Contains("Console.WriteLine("),
            HttpStatusCode.InternalServerError,
            """
            {"error": {"message": "Transmogrifier broken."} }
            """
        );

        var response = await GetPromptWithOpenAIModel(model).ReadLineAsync();

        Assert.Contains("Console.WriteLine(// Error calling OpenAI", response.Text);
    }

    private Prompt GetPromptWithOpenAIModel(string modelName)
    {
        var configuration = new Configuration(openAIConfiguration:
            new OpenAIConfiguration(apiKey: "abc123", prompt: "complete with c#", model: modelName, historyCount: 5, temperature: 0.1, topProbability: null)
        );
        var promptCallbacks = new CSharpReplPromptCallbacks(console, services, configuration, http);
        return new Prompt(console: console.PrettyPromptConsole, callbacks: promptCallbacks, configuration: new PromptConfiguration(keyBindings: configuration.KeyBindings));
    }
}