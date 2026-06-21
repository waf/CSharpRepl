#region License Header
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.
#endregion

using System;
using System.ClientModel;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.AI;
using OpenAI;

namespace CSharpRepl.Services.Completion;

/// <summary>
/// Calls an AI provider to get C# completions. Provider-agnostic: it talks to any OpenAI-compatible
/// endpoint (OpenAI, Anthropic, Azure OpenAI, Gemini, Ollama, ...) through Microsoft.Extensions.AI's
/// <see cref="IChatClient"/> abstraction. Requires an API key, supplied via <c>--aiApiKey</c> or the
/// provider's environment variable (see <see cref="KnownAIProviders"/>).
/// </summary>
public class AICompleteService
{
    public const int DefaultHistoryEntryCount = 5;
    // OpenAI default model. gpt-5.4-mini balances quality, latency, and cost for on-demand code completion.
    public const string DefaultModel = "gpt-5.4-mini";
    public const string DefaultPrompt =
        "// Complete the following C# code that will be run in a REPL. Do not output markdown code fences like ```. "
        + "Prefer functions, statements, and expressions instead of a full program. Prefer modern, terse C# over more verbose C#. "
        + "Never comment what the code prints. Any plain-text, English answers MUST be in a C# comment, and C# code should not be inside comments.";

    private readonly IChatClient? client; // null if no API key is available, which disables the feature.
    private readonly string systemPrompt = DefaultPrompt;
    private readonly int historyCount = DefaultHistoryEntryCount;

    public AICompleteService(AICompletionConfiguration? configuration, IChatClient? chatClient = null)
    {
        if (configuration is null || string.IsNullOrEmpty(configuration.ApiKey))
        {
            return;
        }

        client = chatClient ?? CreateChatClient(configuration);
        systemPrompt = configuration.Prompt ?? DefaultPrompt;
        historyCount = configuration.HistoryCount;
    }

    /// <summary>
    /// Builds an <see cref="IChatClient"/> from the OpenAI SDK pointed at the configured endpoint.
    /// This is the only place that references a concrete provider SDK; everything else is generic.
    /// </summary>
    private static IChatClient CreateChatClient(AICompletionConfiguration configuration)
    {
        var options = new OpenAIClientOptions();
        if (!string.IsNullOrEmpty(configuration.Endpoint))
        {
            options.Endpoint = new Uri(configuration.Endpoint);
        }

        return new OpenAIClient(new ApiKeyCredential(configuration.ApiKey!), options)
            .GetChatClient(configuration.Model)
            .AsIChatClient();
    }

    /// <summary>
    /// Resolves raw command-line values into a concrete <see cref="AICompletionConfiguration"/> using the
    /// known-provider table: the provider supplies defaults for the API-key environment variable, endpoint,
    /// and model, each of which an explicit option overrides. An unrecognized provider name is treated as a
    /// custom endpoint (no environment-variable lookup; <c>--aiEndpoint</c>/<c>--aiModel</c> must be supplied).
    /// </summary>
    public static AICompletionConfiguration CreateConfiguration(
        string? provider, string? apiKey, string? endpoint, string? model, string? prompt, int? historyCount)
    {
        var known = KnownAIProviders.Find(provider ?? KnownAIProviders.DefaultProviderName);

        return new AICompletionConfiguration(
            apiKey: apiKey ?? (known is null ? null : KnownAIProviders.ReadEnvironmentVariable(known.ApiKeyEnvironmentVariable)),
            endpoint: endpoint ?? known?.Endpoint,
            model: model ?? known?.DefaultModel ?? DefaultModel,
            prompt: prompt ?? DefaultPrompt,
            historyCount: historyCount ?? DefaultHistoryEntryCount);
    }

    public async IAsyncEnumerable<string> CompleteAsync(IReadOnlyList<string> submissions, string code, int caret, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        if (client is null)
        {
            yield break;
        }

        // Send the system prompt, then the most recent `historyCount` submissions as context, then the
        // current code as the final user message.
        var messages = new List<ChatMessage> { new(ChatRole.System, systemPrompt) };
        foreach (var submission in submissions.TakeLast(historyCount))
        {
            messages.Add(new(ChatRole.User, submission));
        }
        messages.Add(new(ChatRole.User, code));

        await foreach (var update in client.GetStreamingResponseAsync(messages, cancellationToken: cancellationToken))
        {
            var content = update.Text;
            if (string.IsNullOrEmpty(content))
            {
                continue;
            }
            yield return content.Replace("\t", "    ");
        }
    }
}
