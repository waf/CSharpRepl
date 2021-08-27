// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using CSharpRepl.Services.Roslyn.References;
using System;
using System.Collections.Generic;
using System.IO;

namespace CSharpRepl.Services
{
    /// <summary>
    /// Configuration from command line parameters
    /// </summary>
    public sealed class Configuration
    {
        public HashSet<string> References { get; } = new();
        public HashSet<string> Usings { get; } = new();

        public const string FrameworkDefault = SharedFramework.NetCoreApp;
        public string Framework { get; set; } = FrameworkDefault;

        public string? Theme { get; set; }
        public string? ResponseFile { get; set; }
        public string? LoadScript { get; set; }
        public string[] LoadScriptArgs { get; set; } = Array.Empty<string>();

        public bool ShowVersionAndExit { get; set; }
        public bool ShowHelpAndExit { get; set; }

        public static string ApplicationDirectory => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            ".csharprepl"
        );

        public static IReadOnlyCollection<string> SymbolServers => new[] {
            "https://symbols.nuget.org/download/symbols/",
            "http://msdl.microsoft.com/download/symbols/"
        };
    }
}
