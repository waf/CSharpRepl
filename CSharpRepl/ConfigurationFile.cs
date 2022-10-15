// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.CommandLine;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security;
using static System.Environment;

namespace CSharpRepl;

/// <summary>
/// The CSharpRepl configuration file is an RSP (response) file. This is the same filetype that csi and msbuild use.
/// System.CommandLine supports it natively, so this file is just some helper functions.
/// </summary>
internal static class ConfigurationFile
{
    public static void CreateDefaultConfigurationFile(string configFilePath, RootCommand commandLine, Option[] ignoreCommands)
    {
        try
        {
            var availableOptions = commandLine.Options
                .Where(option => !ignoreCommands.Contains(option))
                .Select(option => $"{NewLine}# {option.Description}{NewLine}# --{option.Name} <{option.ValueType.Name.ToLower()}>");
            File.WriteAllText(
                configFilePath,
                "# Add csharprepl command line options to this file to configure csharprepl." + NewLine +
                "# You may uncomment an option below by removing the leading '#' character." + NewLine +
                string.Join(NewLine, availableOptions)
            );
        }
        catch (Exception ex) when (ex is IOException or SecurityException or UnauthorizedAccessException or DirectoryNotFoundException)
        {
            // If creating the default config file fails, don't consider that fatal, just warn and move on.
            Console.WriteLine("Warning, could not create default configuration file at path: " + configFilePath);
        }
    }

    public static void LaunchEditor(string configFilePath)
    {
        // prefer whatever is in the EDITOR environment variable, then fall back to vscode, then notepad/vim depending on OS.
        try
        {
            var editorName = GetEnvironmentVariable("EDITOR");
            if(editorName is null)
            {
                var vsCodeLocationProcess = Process.Start(new ProcessStartInfo
                {
                    FileName = OperatingSystem.IsWindows() ? "where" : "which",
                    Arguments = "code",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WindowStyle = ProcessWindowStyle.Hidden,
                    RedirectStandardOutput = true,
                });
                vsCodeLocationProcess?.WaitForExit();
                editorName = vsCodeLocationProcess?.ExitCode == 0 ? "code" : null;
            }
            editorName ??= OperatingSystem.IsWindows() ? "notepad.exe" : "vim";
            Process.Start(new ProcessStartInfo
            {
                FileName = editorName,
                Arguments = configFilePath,
                UseShellExecute = OperatingSystem.IsWindows(),
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden
            });
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("Could not launch editor for file: " + configFilePath);
            Console.Error.WriteLine(ex.Message);
        }
    }
}
