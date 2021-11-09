// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using CSharpRepl.Services;
using CSharpRepl.Services.Roslyn.References;
using System;
using System.Collections.Generic;
using System.CommandLine;
using System.CommandLine.Builder;
using System.CommandLine.IO;
using System.CommandLine.Parsing;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using static System.Environment;

namespace CSharpRepl
{
    /// <summary>
    /// Parses command line arguments using System.CommandLine.
    /// Includes support for dotnet-suggest.
    /// </summary>
    internal static class CommandLine
    {
        private const string DisableFurtherOptionParsing = "--";

        private static readonly Option<string[]> References = new(
            aliases: new[] { "--reference", "-r", "/r" },
            description: "Reference assemblies, nuget packages, and csproj files.",
            getDefaultValue: Array.Empty<string>
        );

        private static readonly Option<string[]> Usings = new Option<string[]>(
            aliases: new[] { "--using", "-u", "/u" },
            description: "Add using statements.",
            getDefaultValue: Array.Empty<string>
        ).AddSuggestions(GetAvailableUsings);

        private static readonly Option<string> Framework = new Option<string>(
            aliases: new[] { "--framework", "-f", "/f" },
            description: "Reference a shared framework.",
            getDefaultValue: () => Configuration.FrameworkDefault
        ).AddSuggestions(SharedFramework.SupportedFrameworks);

        private static readonly Option<string> Theme = new(
            aliases: new[] { "--theme", "-t", "/t" },
            description: "Read a theme file for syntax highlighting. Respects the NO_COLOR standard.",
            getDefaultValue: () => string.Empty
        );

        private static readonly Option<bool> Trace = new(
            aliases: new[] { "--trace" },
            description: "Enable a trace log, written to the current directory."
        );

        private static readonly Option<bool> Help = new(
            aliases: new[] { "--help", "-h", "-?", "/h", "/?" },
            description: "Show this help and exit."
        );

        private static readonly Option<bool> Version = new(
            aliases: new[] { "--version", "-v", "/v" },
            description: "Show version number and exit."
        );

        public static Configuration Parse(string[] args)
        {
            var parseArgs = RemoveScriptArguments(args).ToArray();

            Framework.AddValidator(r =>
                SharedFramework.SupportedFrameworks.Any(f => r.GetValueOrDefault<string>().StartsWith(f, StringComparison.OrdinalIgnoreCase))
                ? null // success
                : "Unrecognized --framework value"
            );

            var commandLine =
                new CommandLineBuilder(
                    new RootCommand("C# REPL") { References, Usings, Framework, Theme, Trace, Help, Version }
                )
                .UseSuggestDirective() // support autocompletion via dotnet-suggest
                .Build()
                .Parse(parseArgs);

            if (ShouldExitEarly(commandLine, out var text))
            {
                return new Configuration { OutputForEarlyExit = text };
            }
            if (commandLine.Errors.Count > 0)
            {
                throw new InvalidOperationException(string.Join(NewLine, commandLine.Errors));
            }

            var config = new Configuration
            {
                References = commandLine.ValueForOption(References)?.ToHashSet() ?? new HashSet<string>(),
                Usings = commandLine.ValueForOption(Usings)?.ToHashSet() ?? new HashSet<string>(),
                Framework = commandLine.ValueForOption(Framework) ?? Configuration.FrameworkDefault,
                LoadScript = ProcessScriptArguments(args),
                LoadScriptArgs = commandLine.UnparsedTokens.ToArray(),
                Theme = commandLine.ValueForOption(Theme),
                Trace = commandLine.ValueForOption(Trace)
            };

            return config;
        }

        private static bool ShouldExitEarly(ParseResult commandLine, [NotNullWhen(true)] out string? text)
        {
            if(commandLine.Directives.Any())
            {
                // this is just for dotnet-suggest directive processing. Invoking should write to stdout
                // and should not start the REPL. It's a feature of System.CommandLine.
                var console = new TestConsole();
                commandLine.Invoke(console);
                text = console.Out.ToString() ?? string.Empty;
                return true;
            }
            if (commandLine.ValueForOption<bool>("--help"))
            {
                text = GetHelp();
                return true;
            }
            if (commandLine.ValueForOption<bool>("--version"))
            {
                text = GetVersion();
                return true;
            }

            text = null;
            return false;
        }

        /// <summary>
        /// We allow csx files to be specified, sometimes in ambiguous scenarios that
        /// System.CommandLine can't figure out. So we remove it from processing here,
        /// and process it manually in <see cref="ProcessScriptArguments"/>.
        /// </summary>
        private static IEnumerable<string> RemoveScriptArguments(string[] args)
        {
            bool foundIgnore = false;
            foreach (var arg in args)
            {
                foundIgnore |= arg == DisableFurtherOptionParsing;
                if (foundIgnore || !arg.EndsWith(".csx"))
                {
                    yield return arg;
                }
            }
        }

        /// <summary>
        /// Reads the contents of any provided script (csx) files.
        /// </summary>
        private static string? ProcessScriptArguments(string[] args)
        {
            var stringBuilder = new StringBuilder();
            foreach (var arg in args)
            {
                if (arg == DisableFurtherOptionParsing) break;
                if (!arg.EndsWith(".csx")) continue;
                if (!File.Exists(arg)) throw new FileNotFoundException($@"Script file ""{arg}"" was not found");
                stringBuilder.AppendLine(File.ReadAllText(arg));
            }
            return stringBuilder.Length == 0 ? null : stringBuilder.ToString();
        }

        /// <summary>
        /// Output of --help
        /// </summary>
        /// <remarks>
        /// System.CommandLine can generate the help text for us, but I think it's less
        /// readable, and the code to configure it ends up being longer than the below string.
        /// </remarks>
        private static string GetHelp() =>
            GetVersion() + NewLine +
            "Usage: csharprepl [OPTIONS] [@response-file.rsp] [script-file.csx] [-- <additional-arguments>]" + NewLine + NewLine +
            "Starts a REPL (read eval print loop) according to the provided [OPTIONS]." + NewLine +
            "These [OPTIONS] can be provided at the command line, or via a [@response-file.rsp]." + NewLine +
            "A [script-file.csx], if provided, will be executed before the prompt starts." + NewLine + NewLine +
            "OPTIONS:" + NewLine +
            "  -r <dll> or --reference <dll>:             Reference assemblies, nuget packages, and csproj files." + NewLine +
            "  -u <namespace> or --using <namespace>:     Add using statements." + NewLine +
            "  -f <framework> or --framework <framework>: Reference a shared framework." + NewLine +
            "                                             Available shared frameworks: " + NewLine + GetInstalledFrameworks(
            "                                             ") + NewLine +
            "  -t <theme.json> or --theme <theme.json>:   Read a theme file for syntax highlighting. Respects the NO_COLOR standard." + NewLine +
            "  -v or --version:                           Show version number and exit." + NewLine +
            "  -h or --help:                              Show this help and exit." + NewLine +
            "  --trace:                                   Produce a trace file in the current directory, for CSharpRepl bug reports." + NewLine + NewLine +
            "@response-file.rsp:" + NewLine +
            "  A file, with extension .rsp, containing the above command line [OPTIONS], one option per line." + NewLine + NewLine +
            "script-file.csx:" + NewLine +
            "  A file, with extension .csx, containing lines of C# to evaluate before starting the REPL." + NewLine +
            "  Arguments to this script can be passed as <additional-arguments> and will be available in a global `args` variable." + NewLine;

        /// <summary>
        /// Get assembly version for usage in --version
        /// </summary>
        private static string GetVersion()
        {
            var product = "C# REPL";
            var version = Assembly
                .GetExecutingAssembly()
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
                ?.InformationalVersion ?? "unversioned";
            return product + " " + version;
        }

        /// <summary>
        /// In the help text, lists the available frameworks and marks one as default.
        /// </summary>
        private static string GetInstalledFrameworks(string leftPadding)
        {
            var frameworkList = SharedFramework
                .SupportedFrameworks
                .Select(fx => leftPadding + "- " + fx + (fx == Configuration.FrameworkDefault ? " (default)" : ""));
            return string.Join(NewLine, frameworkList);
        }

        /// <summary>
        /// Autocompletions for --using.
        /// </summary>
        private static IEnumerable<string> GetAvailableUsings(ParseResult? parseResult, string? textToMatch)
        {
            if (string.IsNullOrEmpty(textToMatch) || "Syste".StartsWith(textToMatch, StringComparison.OrdinalIgnoreCase))
                return new[] { "System" };

            if (!textToMatch.StartsWith("System", StringComparison.OrdinalIgnoreCase))
                return Array.Empty<string>();

            var runtimeAssemblyPaths = Directory.GetFiles(RuntimeEnvironment.GetRuntimeDirectory(), "*.dll");
            using var mlc = new MetadataLoadContext(new PathAssemblyResolver(runtimeAssemblyPaths));

            var namespaces =
                from assembly in runtimeAssemblyPaths
                from type in GetTypes(assembly)
                where type.IsPublic
                      && type.Namespace is not null
                      && type.Namespace.StartsWith(textToMatch, StringComparison.OrdinalIgnoreCase)
                select type.Namespace;

            return namespaces.Distinct().Take(16).ToArray();

            IEnumerable<Type> GetTypes(string assemblyPath)
            {
                try { return mlc.LoadFromAssemblyPath(assemblyPath).GetTypes(); }
                catch (BadImageFormatException) { return Array.Empty<Type>(); } // handle native DLLs that have no managed metadata.
            }
        }
    }
}
