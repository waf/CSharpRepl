// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Collections.Generic;
using System.Threading.Tasks;
using CSharpRepl.Services;
using CSharpRepl.Services.Completion;
using Xunit;

namespace CSharpRepl.Tests;

public class OpenAICompleteServiceTests
{
    [Fact]
    public async Task CompleteAsync_NoConfiguration_YieldsNothing()
    {
        var service = new OpenAICompleteService(configuration: null);

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
        var service = new OpenAICompleteService(new OpenAIConfiguration(apiKey: "", prompt: "p", model: "gpt-4o", historyCount: 5));

        var results = new List<string>();
        await foreach (var chunk in service.CompleteAsync(["previous submission"], "1 + ", caret: 4, cancellationToken: TestContext.Current.CancellationToken))
        {
            results.Add(chunk);
        }

        Assert.Empty(results);
    }

    [Fact]
    public void Constructor_WithApiKey_DoesNotThrow()
    {
        // Constructing the underlying ChatClient does not make a network call; only completing would.
        var service = new OpenAICompleteService(
            new OpenAIConfiguration(apiKey: "sk-fake-key-for-construction-only", prompt: "p", model: "gpt-4o", historyCount: 5));

        Assert.NotNull(service);
    }
}
