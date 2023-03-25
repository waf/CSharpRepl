using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace CSharpRepl.Services.Completion.OpenAI.ChatCompletionApi;

/// <summary>
/// Issue an API call to the "Chat Completion" API (GPT-series models)
/// </summary>
internal sealed class ChatCompletionApiClient : IOpenAIClient
{
    private readonly HttpClient httpClient;
    private readonly JsonSerializerOptions jsonSerializerOptions;
    private readonly OpenAIConfiguration configuration;
    private const int CharacterToTokenEstimate = 3;

    public bool EmitLeadingNewline => true;

    public ChatCompletionApiClient(HttpClient httpClient, JsonSerializerOptions jsonSerializerOptions, OpenAIConfiguration configuration)
    {
        this.httpClient = httpClient;
        this.jsonSerializerOptions = jsonSerializerOptions;
        this.configuration = configuration;
    }

    public Task<HttpResponseMessage> IssueRequestAsync(IReadOnlyList<string> submissions, string code, int caret, CancellationToken cancellationToken)
    {
        int previousSubmissionCount;
        int maxTokens = 0;
        for (previousSubmissionCount = 5; maxTokens <= 0 || previousSubmissionCount == 0; previousSubmissionCount--)
        {
            maxTokens = 4097
                - configuration.Prompt.Length / CharacterToTokenEstimate
                - submissions.TakeLast(previousSubmissionCount).Sum(s => s.Length) / CharacterToTokenEstimate
                - code.Length / CharacterToTokenEstimate;
        }

        if (maxTokens <= 0)
        {
            throw new OpenAIException("Prompt context exceeded! Too much code to send to OpenAI.");
        }

        var request = new ChatCompletionRequest
        {
            Model = configuration.Model,
            Messages = new[] { new Message("system", configuration.Prompt), }
                .Concat(submissions.TakeLast(previousSubmissionCount).Select(submission => new Message("user", submission)))
                .Append(new Message("user", code))
                .ToArray(),
            MaxTokens = maxTokens,
            Temperature = configuration.Temperature,
            TopProbability = configuration.TopProbability,
            Stream = true,
            User = "CSharpRepl"
        };

        return httpClient.PostAsJsonAsync("https://api.openai.com/v1/chat/completions", request, jsonSerializerOptions, cancellationToken);
    }

    public string? ParseLineToCompletion(string line) =>
        JsonSerializer.Deserialize<ChatCompletionResponse>(line)?.Choices.FirstOrDefault()?.Delta?.Content;
}
