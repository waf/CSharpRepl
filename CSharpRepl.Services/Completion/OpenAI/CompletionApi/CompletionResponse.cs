#region License Header
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.
#endregion

using System.Text.Json.Serialization;

namespace CSharpRepl.Services.Completion.OpenAI.CompletionApi;

public sealed class CompletionResponse
{
    [JsonPropertyName("id")]
    public required string Id { get; set; }

    [JsonPropertyName("choices")]
    public required Choice[] Choices { get; set; }

    [JsonPropertyName("object")]
    public string? Object { get; set; }

    [JsonPropertyName("created")]
    public int Created { get; set; }

    [JsonPropertyName("model")]
    public string? Model { get; set; }

    public class Choice
    {
        [JsonPropertyName("text")]
        public required string Text { get; set; }
        [JsonPropertyName("index")]
        public int Index { get; set; }
        [JsonPropertyName("logprobs")]
        public object? LogProbs { get; set; }
        [JsonPropertyName("finish_reason")]
        public string? FinishReason { get; set; }
    }
}

/// <summary>
/// Root of the error response from the OpenAI Completion API
/// </summary>
public sealed class ErrorResponse
{
    [JsonPropertyName("error")]
    public required ApiError Error { get; set; }

    /// <summary>
    /// Error object returned from the OpenAI Completion API
    /// </summary>
    public class ApiError
    {
        [JsonPropertyName("message")]
        public required string Message { get; set; }
        [JsonPropertyName("type")]
        public string? Type { get; set; }
        [JsonPropertyName("param")]
        public string? Param { get; set; }
        [JsonPropertyName("code")]
        public string? Code { get; set; }
    }
}
