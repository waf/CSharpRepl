#region License Header
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.
#endregion

using System;
using System.Collections.Generic;
using System.Linq;

namespace CSharpRepl.Services.Completion;

/// <summary>
/// A known AI provider: the environment variable its API key is read from, the default
/// OpenAI-compatible endpoint to talk to (<see langword="null"/> means the OpenAI SDK default),
/// and a sensible default model.
/// </summary>
public sealed record KnownAIProvider(string Name, string ApiKeyEnvironmentVariable, string? Endpoint, string DefaultModel);

/// <summary>
/// The set of providers csharprepl knows about out of the box. This table is the only place a
/// provider is named; everything that consumes it (<see cref="AICompleteService"/>, the CLI) stays
/// generic. Any provider exposing an OpenAI-compatible <c>/chat/completions</c> endpoint can be
/// added as a single row, and users can always target one not listed here via <c>--aiEndpoint</c>.
/// </summary>
public static class KnownAIProviders
{
    public const string DefaultProviderName = "openai";

    // Each Endpoint is a base URL; the OpenAI SDK appends "/chat/completions" (normalizing the slash, so a
    // trailing slash is fine). A null Endpoint uses the SDK's built-in default (api.openai.com). Default
    // models drift over time and are easily overridden with --aiModel; these are sensible "just works" picks.
    public static IReadOnlyList<KnownAIProvider> All { get; } =
    [
        new("openai", "OPENAI_API_KEY", Endpoint: null, DefaultModel: AICompleteService.DefaultModel),
        new("anthropic", "ANTHROPIC_API_KEY", Endpoint: "https://api.anthropic.com/v1/", DefaultModel: "claude-sonnet-4-6"),
        new("grok", "XAI_API_KEY", Endpoint: "https://api.x.ai/v1", DefaultModel: "grok-4.3"),
        new("deepseek", "DEEPSEEK_API_KEY", Endpoint: "https://api.deepseek.com/v1", DefaultModel: "deepseek-chat"),
        new("gemini", "GEMINI_API_KEY", Endpoint: "https://generativelanguage.googleapis.com/v1beta/openai/", DefaultModel: "gemini-2.5-flash"),
        new("mistral", "MISTRAL_API_KEY", Endpoint: "https://api.mistral.ai/v1", DefaultModel: "mistral-large-latest"),
        // Codestral is Mistral's code-specialized model, served from a dedicated endpoint with its own key.
        new("codestral", "CODESTRAL_API_KEY", Endpoint: "https://codestral.mistral.ai/v1", DefaultModel: "codestral-latest"),
    ];

    /// <summary>Looks up a provider by name (case-insensitive). Returns <see langword="null"/> for an unknown name.</summary>
    public static KnownAIProvider? Find(string? name) =>
        All.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Reads an environment variable from the Process scope, falling back to the User scope (which is
    /// meaningful on Windows; a no-op elsewhere). Mirrors how the old OpenAI-only path resolved its key.
    /// </summary>
    public static string? ReadEnvironmentVariable(string name) =>
        Environment.GetEnvironmentVariable(name, EnvironmentVariableTarget.Process)
        ?? Environment.GetEnvironmentVariable(name, EnvironmentVariableTarget.User);
}
