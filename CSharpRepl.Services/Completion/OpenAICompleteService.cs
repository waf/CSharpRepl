#region License Header
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.
#endregion

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using OpenAI.Chat;

namespace CSharpRepl.Services.Completion;

/// <summary>
/// Call the OpenAI API to get C# completions. Requires an OpenAI API token (which can be purchased from OpenAI).
/// </summary>
public class OpenAICompleteService
{
    public const int DefaultHistoryEntryCount = 5;
    public const string DefaultModel = "gpt-4o";
    public const string DefaultPrompt =
        "// Complete the following C# code that will be run in a REPL. Do not output markdown code fences like ```. "
        + "Prefer functions, statements, and expressions instead of a full program. Prefer modern, terse C# over more verbose C#. "
        + "Never comment what the code prints. Any plain-text, English answers MUST be in a C# comment, and C# code should not be inside comments.";
    public const string ApiKeyEnvironmentVariableName = "OPENAI_API_KEY";

    private readonly ChatClient? client; // null if no Open AI API key is available.
    private readonly SystemChatMessage? prompt;

    public OpenAICompleteService(OpenAIConfiguration? configuration, ChatClient? chatClient = null)
    {
        if (configuration is null || string.IsNullOrEmpty(configuration.ApiKey))
        {
            return;
        }

        client = chatClient ?? new ChatClient(configuration.Model, configuration.ApiKey);
        prompt = new SystemChatMessage(configuration.Prompt ?? DefaultPrompt);
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

        var input = new ChatMessage[] { prompt! }
            .Concat(submissions.Append(code).Select(s => new UserChatMessage(s)))
            .ToArray();

        var output = client.CompleteChatStreamingAsync(input, cancellationToken: cancellationToken);

        await foreach (var update in output.WithCancellation(cancellationToken))
        {
            if (update is null or { ContentUpdate.Count: 0 })
            {
                continue;
            }
            var content = update.ContentUpdate[0].Text;
            if (string.IsNullOrEmpty(content))
            {
                yield return "\n";
            }
            yield return content.Replace("\t", "    ");
        }
    }
}
