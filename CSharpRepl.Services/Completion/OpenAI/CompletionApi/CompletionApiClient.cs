#region License Header
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.
#endregion

using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace CSharpRepl.Services.Completion.OpenAI.CompletionApi;

/// <summary>
/// Issue an API call to the "Completion" API
/// </summary>
internal sealed class CompletionApiClient : IOpenAIClient
{
    private readonly HttpClient httpClient;
    private readonly JsonSerializerOptions jsonSerializerOptions;
    private readonly OpenAIConfiguration configuration;
    private const int CharacterToTokenEstimate = 2;
    private const int TokenLength = 4090; // a bit under to give us some leeway

    public CompletionApiClient(HttpClient httpClient, JsonSerializerOptions jsonSerializerOptions, OpenAIConfiguration configuration)
    {
        this.httpClient = httpClient;
        this.jsonSerializerOptions = jsonSerializerOptions;
        this.configuration = configuration;
    }

    /// <summary>
    /// By default, send 5 history entries to the API as context. However, if that's too long and we end up exceeding our token count, reduce the context.
    /// </summary>
    public Task<HttpResponseMessage> IssueRequestAsync(IReadOnlyList<string> submissions, string code, int caret, CancellationToken cancellationToken)
    {
        string currentCodePrefix = code[..caret];
        string currentCodeSuffix = code[caret..];
        string prompt = "";
        int maxTokens = 0;
        for (int previousSubmissionContextCount = 5; maxTokens <= 0 || previousSubmissionContextCount == 0; previousSubmissionContextCount--)
        {
            prompt = configuration.Prompt + "\n" + string.Join("\n", submissions.TakeLast(previousSubmissionContextCount)) + "\n" + currentCodePrefix;
            maxTokens = TokenLength
                - prompt.Length / CharacterToTokenEstimate
                - currentCodeSuffix.Length / CharacterToTokenEstimate;
        }

        if (maxTokens <= 0)
        {
            throw new OpenAIException("Prompt context exceeded! Too much code to send to OpenAI.");
        }

        var request = new CompletionRequest
        {
            Model = configuration.Model,
            Prompt = prompt,
            Suffix = currentCodeSuffix,
            MaxTokens = maxTokens,
            Temperature = configuration.Temperature,
            TopProbability = configuration.TopProbability,
            Stream = true,
            User = "CSharpRepl"
        };

        return httpClient.PostAsJsonAsync("https://api.openai.com/v1/completions", request, jsonSerializerOptions, cancellationToken);
    }

    public string? ParseLineToCompletion(string line) =>
        JsonSerializer.Deserialize<CompletionResponse>(line)?.Choices.FirstOrDefault()?.Text;
}
