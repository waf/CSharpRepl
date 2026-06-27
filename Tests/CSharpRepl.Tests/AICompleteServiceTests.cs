// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

#nullable enable

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using CSharpRepl.Services;
using CSharpRepl.Services.Completion;
using Microsoft.Extensions.AI;
using Xunit;

namespace CSharpRepl.Tests;

public class AICompleteServiceTests
{
    [Fact]
    public async Task CompleteAsync_NoConfiguration_YieldsNothing()
    {
        var service = new AICompleteService(configuration: null);

        var results = new List<string>();
        await foreach (var chunk in service.CompleteAsync([], "Console.Wri", caret: 11, cancellationToken: TestContext.Current.CancellationToken))
        {
            results.Add(chunk);
        }

        Assert.Empty(results);
    }

    [Fact]
    public async Task CompleteAsync_EmptyApiKey_YieldsNothing()
    {
        var service = new AICompleteService(new AICompletionConfiguration(apiKey: "", endpoint: null, model: "gpt-4o", prompt: "p", historyCount: 5));

        var results = new List<string>();
        await foreach (var chunk in service.CompleteAsync(["previous submission"], "1 + ", caret: 4, cancellationToken: TestContext.Current.CancellationToken))
        {
            results.Add(chunk);
        }

        Assert.Empty(results);
    }

    [Theory]
    [InlineData(null)]                              // OpenAI default endpoint
    [InlineData("https://api.anthropic.com/v1/")]   // custom OpenAI-compatible endpoint
    public void Constructor_WithApiKey_DoesNotThrow(string? endpoint)
    {
        // Building the underlying chat client does not make a network call; only completing would.
        var service = new AICompleteService(
            new AICompletionConfiguration(apiKey: "sk-fake-key-for-construction-only", endpoint: endpoint, model: "some-model", prompt: "p", historyCount: 5));

        Assert.NotNull(service);
    }

    [Fact]
    public async Task CompleteAsync_WithInjectedChatClient_StreamsChunksTabExpandedAndSkipsEmpty()
    {
        // The IChatClient abstraction makes the network seam trivially fakeable (the old concrete-ChatClient
        // design could not). Tabs are expanded to four spaces and empty updates are skipped.
        var fake = new FakeChatClient("var x", "", "\t= 1;");
        var service = new AICompleteService(
            new AICompletionConfiguration(apiKey: "key", endpoint: null, model: "any", prompt: "p", historyCount: 5),
            fake);

        var results = new List<string>();
        await foreach (var chunk in service.CompleteAsync([], "var ", caret: 4, cancellationToken: TestContext.Current.CancellationToken))
        {
            results.Add(chunk);
        }

        Assert.Equal(new[] { "var x", "    = 1;" }, results);
    }

    [Fact]
    public async Task CompleteAsync_WhenProviderThrows_YieldsErrorCommentInsteadOfThrowing()
    {
        // A provider failure (e.g. HTTP 429 quota exceeded) must not crash the REPL: it surfaces as a C# comment.
        var fake = new ThrowingChatClient(new InvalidOperationException("HTTP 429 (insufficient_quota)\n\nYou exceeded your current quota."));
        var service = new AICompleteService(
            new AICompletionConfiguration(apiKey: "key", endpoint: null, model: "any", prompt: "p", historyCount: 5),
            fake);

        var results = new List<string>();
        await foreach (var chunk in service.CompleteAsync([], "var ", caret: 4, cancellationToken: TestContext.Current.CancellationToken))
        {
            results.Add(chunk);
        }

        var combined = string.Concat(results);
        Assert.Contains("// AI completion failed:", combined);
        Assert.Contains("HTTP 429 (insufficient_quota)", combined);
        Assert.DoesNotContain("\n//", combined.TrimStart()); // multi-line message collapsed to a single comment line
    }

    [Fact]
    public async Task CompleteAsync_WhenProviderThrowsMidStream_YieldsPartialOutputThenErrorComment()
    {
        var fake = new ThrowingChatClient(new InvalidOperationException("boom"), "var x", "= 1;");
        var service = new AICompleteService(
            new AICompletionConfiguration(apiKey: "key", endpoint: null, model: "any", prompt: "p", historyCount: 5),
            fake);

        var results = new List<string>();
        await foreach (var chunk in service.CompleteAsync([], "var ", caret: 4, cancellationToken: TestContext.Current.CancellationToken))
        {
            results.Add(chunk);
        }

        Assert.Equal("var x", results[0]);
        Assert.Equal("= 1;", results[1]);
        Assert.EndsWith("// AI completion failed: boom", results[^1]);
    }

    [Fact]
    public async Task CompleteAsync_WhenCancelled_YieldsNothing()
    {
        // Cancellation is a normal stop, not an error: it must not insert an error comment.
        var fake = new FakeChatClient("var x", "= 1;");
        var service = new AICompleteService(
            new AICompletionConfiguration(apiKey: "key", endpoint: null, model: "any", prompt: "p", historyCount: 5),
            fake);

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var results = new List<string>();
        await foreach (var chunk in service.CompleteAsync([], "var ", caret: 4, cancellationToken: cts.Token))
        {
            results.Add(chunk);
        }

        Assert.Empty(results);
    }

    [Theory]
    [InlineData(null, null, null)]            // unspecified provider defaults to OpenAI's default model/endpoint
    [InlineData("openai", null, null)]
    [InlineData("ANTHROPIC", "claude-sonnet-4-6", "https://api.anthropic.com/v1/")] // case-insensitive
    [InlineData("grok", "grok-4.3", "https://api.x.ai/v1")]
    [InlineData("deepseek", "deepseek-chat", "https://api.deepseek.com/v1")]
    [InlineData("gemini", "gemini-2.5-flash", "https://generativelanguage.googleapis.com/v1beta/openai/")]
    [InlineData("mistral", "mistral-large-latest", "https://api.mistral.ai/v1")]
    [InlineData("codestral", "codestral-latest", "https://codestral.mistral.ai/v1")]
    public void CreateConfiguration_UsesProviderDefaults(string? provider, string? expectedModel, string? expectedEndpoint)
    {
        // apiKey supplied explicitly so the result is independent of the test host's environment variables.
        var config = AICompleteService.CreateConfiguration(provider, apiKey: "k", endpoint: null, model: null, prompt: null, historyCount: null);

        Assert.Equal(expectedModel ?? AICompleteService.DefaultModel, config.Model);
        Assert.Equal(expectedEndpoint, config.Endpoint);
        Assert.Equal(AICompleteService.DefaultPrompt, config.Prompt);
        Assert.Equal(AICompleteService.DefaultHistoryEntryCount, config.HistoryCount);
    }

    [Fact]
    public void CreateConfiguration_ExplicitValuesOverrideProviderDefaults()
    {
        var config = AICompleteService.CreateConfiguration(
            provider: "anthropic", apiKey: "explicit", endpoint: "https://example.test/v1/", model: "my-model", prompt: "custom", historyCount: 2);

        Assert.Equal("explicit", config.ApiKey);
        Assert.Equal("https://example.test/v1/", config.Endpoint);
        Assert.Equal("my-model", config.Model);
        Assert.Equal("custom", config.Prompt);
        Assert.Equal(2, config.HistoryCount);
    }

    [Fact]
    public void CreateConfiguration_UnknownProvider_DoesNotProbeEnvironmentAndFallsBackToDefaultModel()
    {
        var config = AICompleteService.CreateConfiguration(
            provider: "some-custom-provider", apiKey: null, endpoint: "https://local.test/v1/", model: null, prompt: null, historyCount: null);

        Assert.Null(config.ApiKey); // unknown provider => no environment variable is probed
        Assert.Equal("https://local.test/v1/", config.Endpoint);
        Assert.Equal(AICompleteService.DefaultModel, config.Model);
    }

    private sealed class FakeChatClient(params string[] chunks) : IChatClient
    {
        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
            => Stream(cancellationToken);

        private async IAsyncEnumerable<ChatResponseUpdate> Stream([EnumeratorCancellation] CancellationToken cancellationToken)
        {
            foreach (var chunk in chunks)
            {
                cancellationToken.ThrowIfCancellationRequested();
                yield return new ChatResponseUpdate(ChatRole.Assistant, chunk);
                await Task.Yield();
            }
        }

        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose() { }
    }

    private sealed class ThrowingChatClient(Exception toThrow, params string[] chunksBeforeThrow) : IChatClient
    {
        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
            => Stream();

        private async IAsyncEnumerable<ChatResponseUpdate> Stream()
        {
            foreach (var chunk in chunksBeforeThrow)
            {
                yield return new ChatResponseUpdate(ChatRole.Assistant, chunk);
                await Task.Yield();
            }
            await Task.Yield();
            throw toThrow;
        }

        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose() { }
    }
}
