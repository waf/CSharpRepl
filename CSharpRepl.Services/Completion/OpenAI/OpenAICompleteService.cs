#region License Header
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.
#endregion

using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using CSharpRepl.Services.Completion.OpenAI.ChatCompletionApi;
using CSharpRepl.Services.Completion.OpenAI.CompletionApi;

namespace CSharpRepl.Services.Completion.OpenAI;

/// <summary>
/// Call the OpenAI API to get C# completions. Requires an OpenAI API token (which can be purchased from OpenAI).
/// </summary>
public class OpenAICompleteService
{
    public const int DefaultHistoryEntryCount = 5;
    public const double DefaultTemperature = 0.1;
    public const string DefaultModel = "text-davinci-003";
    public const string DefaultPrompt = "// Complete the following C# code that will be run in a REPL. Prefer functions, statements, and expressions instead of a full program and do not include any namespace declarations. Do not comment what the code prints. Any plain-text, english answers must be in a C# comment.";
    public const string ApiKeyEnvironmentVariableName = "OPENAI_API_KEY";

    private readonly IOpenAIClient? client; // null if no Open AI API key is available.

    public OpenAICompleteService(OpenAIConfiguration configuration, HttpMessageHandler? httpMessageHandler = null)
    {
        if (configuration is null || string.IsNullOrEmpty(configuration.ApiKey))
        {
            return;
        }

        var httpClient = httpMessageHandler is null ? new HttpClient() : new HttpClient(httpMessageHandler);
        httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", configuration.ApiKey);
        var jsonSerializerOptions = new JsonSerializerOptions
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault
        };
        this.client = configuration.Model.StartsWith("gpt", StringComparison.InvariantCultureIgnoreCase)
            ? new ChatCompletionApiClient(httpClient, jsonSerializerOptions, configuration)
            : new CompletionApiClient(httpClient, jsonSerializerOptions, configuration);
    }

    public static string? ApiKey =>
        Environment.GetEnvironmentVariable(ApiKeyEnvironmentVariableName, EnvironmentVariableTarget.Process)
        ?? Environment.GetEnvironmentVariable(ApiKeyEnvironmentVariableName, EnvironmentVariableTarget.User);

    public async IAsyncEnumerable<string> CompleteAsync(IReadOnlyList<string> submissions, string code, int caret, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        if (client is null)
        {
            yield break;
        }

        var (inputStream, error) = await CallOpenAIAsync(submissions, code, caret, cancellationToken).ConfigureAwait(false);
        if (error is not null || inputStream is null)
        {
            yield return $"// Error calling OpenAI:\n {error}";
            yield break;
        }

        if (client.EmitLeadingNewline && code.Length == caret && caret > 0 && code[caret - 1] != '\n')
        {
            // GPT models are "conversational" and assume their output is on a newline after the input, without actually returning a newline.
            // The davinci models don't have this limitation and don't need the newline.
            yield return "\n";
        }

        // because we sent "stream: true" in the API request, the completions are streamed back to us.
        using var reader = new StreamReader(inputStream);
        string? line;
        while ((line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false)) != null)
        {
            line = line?.Trim();
            if (string.IsNullOrEmpty(line))
            {
                continue;
            }
            else if (line.StartsWith("data: "))
            {
                line = line.Substring("data: ".Length);
            }
            else if (line.StartsWith("{")) // not actually streaming a response, assume it's an error. Theoretically this shouldn't happen for HTTP 200, but handle it anyway.
            {
                await ThrowOpenAIException(reader, line, cancellationToken).ConfigureAwait(false);
            }

            if (line == "[DONE]")
            {
                yield break;
            }
            else if (!string.IsNullOrWhiteSpace(line))
            {
                var completion = client.ParseLineToCompletion(line);
                if (!string.IsNullOrEmpty(completion))
                {
                    yield return completion.Replace("\t", "    ");
                }
            }
        }
    }

    private async Task<(Stream?, string? error)> CallOpenAIAsync(IReadOnlyList<string> submissions, string code, int caret, CancellationToken cancellationToken)
    {
        // convert exception to tuple because we're called by an iterator, and C# doesn't support 'yield break' when catching exceptions.
        try
        {
            var response = await IssueApiRequestAndEnsureSuccessful(submissions, code, caret, cancellationToken).ConfigureAwait(false);
            var inputStream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            return (inputStream, null);
        }
        catch (Exception ex)
        {
            return (null, ex.Message);
        }
    }

    private async Task<HttpResponseMessage> IssueApiRequestAndEnsureSuccessful(IReadOnlyList<string> submissions, string code, int caret, CancellationToken cancellationToken)
    {
        var response = await client!.IssueRequestAsync(submissions, code, caret, cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            throw new OpenAIException(await response.Content.ReadAsStringAsync(cancellationToken));
        }

        return response;
    }

    private static async Task ThrowOpenAIException(StreamReader reader, string? line, CancellationToken cancellationToken)
    {
        var restOfMessage = await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
        var completeMessage = line + restOfMessage;
        var error = JsonSerializer.Deserialize<ErrorResponse>(completeMessage);
        throw new OpenAIException(error?.Error.Message ?? "Unknown response from OpenAI API: " + completeMessage);
    }
}
