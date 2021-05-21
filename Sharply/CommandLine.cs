using Sharply.Services;
using Sharply.Services.Roslyn;
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using static System.Environment;

namespace Sharply
{
    /// <summary>
    /// Parses command line arguments.
    /// </summary>
    /// <remarks>
    /// Doing our own parsing, instead of using an existing library, is questionable. However, I wasn't able to find an
    /// existing library that covers the following:
    ///     - Supports response files (i.e. ".rsp" files) for compatibility with other interactive C# consoles (e.g. csi).
    ///     - Supports windows-style forward slash arguments (e.g. /u), again for compatibility with other C# consoles.
    /// </remarks>
    class CommandLine
    {
        public static Configuration ParseArguments(string[] args, Configuration existingConfiguration = null)
        {
            string currentSwitch = "";
            var config = args
                .Aggregate(existingConfiguration ?? new Configuration(), (config, arg) =>
                {
                    //
                    // process option flags
                    //
                    if (arg == "-v" || arg == "--version" || arg == "/v")
                        config.ShowVersionAndExit = true;
                    else if (arg == "-h" || arg == "--help" || arg == "/h" || arg == "/?" || arg == "-?")
                        config.ShowHelpAndExit = true;
                    //
                    // process option names, and optional values if they're provided like /r:Reference
                    //
                    else if (arg.StartsWith("-r") || arg.StartsWith("--reference") || arg.StartsWith("/r"))
                    {
                        currentSwitch = "--reference";
                        if(TryGetConcatenatedValue(arg, out string reference))
                        {
                            config.References.Add(reference);
                        }
                    }
                    else if (arg.StartsWith("-u") || arg.StartsWith("--using") || arg.StartsWith("/u"))
                    { 
                        currentSwitch = "--using";
                        if(TryGetConcatenatedValue(arg, out string usingNamespace))
                        {
                            config.Usings.Add(usingNamespace);
                        }
                    }
                    else if (arg.StartsWith("-f") || arg.StartsWith("--framework") || arg.StartsWith("/f"))
                    { 
                        currentSwitch = "--framework";
                        if(TryGetConcatenatedValue(arg, out string framework))
                        {
                             config.Framework = framework;
                        }
                    }
                    else if (arg.StartsWith("-t") || arg.StartsWith("--theme") || arg.StartsWith("/t"))
                    { 
                        currentSwitch = "--theme";
                        if(TryGetConcatenatedValue(arg, out string theme))
                        {
                             config.Theme = theme;
                        }
                    }
                    //
                    // process option values
                    //
                    else if (currentSwitch == "--reference")
                        config.References.Add(arg);
                    else if (currentSwitch == "--using")
                        config.Usings.Add(arg);
                    else if (currentSwitch == "--framework")
                        config.Framework = arg;
                    else if (currentSwitch == "--theme")
                        config.Theme = arg;
                    // 
                    // Process positional parameters
                    // 
                    else if (arg.EndsWith(".csx"))
                    {
                        if (!File.Exists(arg)) throw new FileNotFoundException($@"Script file ""{arg}"" was not found");
                        config.LoadScript = File.ReadAllText(arg);
                    }
                    else if (arg.EndsWith(".rsp"))
                    {
                        string path = arg.TrimStart('@'); // a common convention is to prefix rsp files with '@'
                        if (!File.Exists(path)) throw new FileNotFoundException($@"RSP file ""{path}"" was not found");
                        if (existingConfiguration is not null) throw new InvalidOperationException("Response files cannot be nested.");
                        var responseFile = File
                            .ReadAllText(path)
                            .Split(new[] { ' ', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.None);
                        config = ParseArguments(responseFile, config);
                    }
                    else
                        throw new InvalidOperationException("Unknown command line option: " + arg);
                    return config;
                });

            if (!SharedFramework.SupportedFrameworks.Contains(config.Framework))
            {
                throw new ArgumentException("Unknown Framework: " + config.Framework + ". Expected one of " + string.Join(", ", SharedFramework.SupportedFrameworks));
            }

            return config;
        }

        /// <summary>
        /// Parse value in a string like "/u:foo"
        /// </summary>
        private static bool TryGetConcatenatedValue(string arg, out string value)
        {
            var referenceValue = arg.Split(new[] { ':', '=' }, 2);
            if (referenceValue.Length == 2)
            {
                value = referenceValue[1];
                return true;
            }
            value = null;
            return false;
        }

        public static string GetHelp() =>
            GetVersion() + NewLine +
            "Usage: sharply [OPTIONS] [response-file.rsp] [script-file.csx]" + NewLine + NewLine +
            "Starts a REPL (read eval print loop) according to the provided [OPTIONS]." + NewLine +
            "These [OPTIONS] can be provided at the command line, or via a [response-file.rsp]." + NewLine +
            "A [script-file.csx], if provided, will be executed before the prompt starts." + NewLine + NewLine +
            "OPTIONS:" + NewLine +
            "  -r <dll> or --reference <dll>:             Add an assembly reference. May be specified multiple times." + NewLine +
            "  -u <namespace> or --using <namespace>:     Add a using statement. May be specified multiple times." + NewLine +
            "  -f <framework> or --framework <framework>: Reference a shared framework. May be specified multiple times." + NewLine +
            "                                             Available shared frameworks: " + NewLine + GetInstalledFrameworks(
            "                                             ") + NewLine +
            "  -t <theme.json> or --theme <theme.json>:   Read a theme file for syntax highlighting." + NewLine +
            "  -v or --version:                           Show version number and exit." + NewLine +
            "  -h or --help:                              Show this help and exit." + NewLine + NewLine +
            "response-file.rsp:" + NewLine +
            "  A file, with extension .rsp, containing the above command line [OPTIONS], one option per line." + NewLine + NewLine +
            "script-file.csx:" + NewLine +
            "  A file, with extension .csx, containing lines of C# to evaluate before starting the REPL." + NewLine;

        public static string GetVersion()
        {
            var product = nameof(Sharply);
            var version = Assembly
                .GetEntryAssembly()
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
                .InformationalVersion;
            return product + " " + version;
        }

        private static string GetInstalledFrameworks(string leftPadding)
        {
            var frameworkList = SharedFramework
                .SupportedFrameworks
                .Select(fx => leftPadding + "- " + fx + (fx == Configuration.FrameworkDefault ? " (default)" : ""));
            return string.Join(NewLine, frameworkList);
        }
    }
}
