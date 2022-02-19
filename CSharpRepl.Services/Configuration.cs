// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using CSharpRepl.Services.Roslyn.References;
using CSharpRepl.Services.Theming;

namespace CSharpRepl.Services;

/// <summary>
/// Configuration from command line parameters
/// </summary>
public sealed class Configuration
{
    public const string FrameworkDefault = SharedFramework.NetCoreApp;

    public static readonly string ApplicationDirectory =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), ".csharprepl");

    public static readonly IReadOnlyCollection<string> SymbolServers = new[]
    {
        "https://symbols.nuget.org/download/symbols/",
        "http://msdl.microsoft.com/download/symbols/"
    };

    public HashSet<string> References { get; }
    public HashSet<string> Usings { get; }
    public string Framework { get; }
    public bool Trace { get; }
    public Theme Theme { get; }
    public string? LoadScript { get; }
    public string[] LoadScriptArgs { get; }
    public string? OutputForEarlyExit { get; }

    public Configuration(
        string[]? references = null,
        string[]? usings = null,
        string? framework = null,
        bool trace = false,
        string? theme = null,
        string? loadScript = null,
        string[]? loadScriptArgs = null,
        string? outputForEarlyExit = null)
    {
        References = references?.ToHashSet() ?? new HashSet<string>();
        Usings = usings?.ToHashSet() ?? new HashSet<string>();
        Framework = framework ?? FrameworkDefault;
        Trace = trace;

        Theme =
            string.IsNullOrEmpty(theme) ?
            Theme.DefaultTheme :
             JsonSerializer.Deserialize<Theme>(
                File.ReadAllText(theme),
                new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }
              ) ?? Theme.DefaultTheme;

        LoadScript = loadScript;
        LoadScriptArgs = loadScriptArgs ?? Array.Empty<string>();
        OutputForEarlyExit = outputForEarlyExit;
    }
}