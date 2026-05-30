// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.IO;
using Xunit;

namespace CSharpRepl.Tests;

public class ConfigurationFileTests
{
    /// <summary>
    /// When no configuration file exists yet, <see cref="CommandLine.Parse"/> writes a default
    /// commented-out .rsp file listing the available options. This drives that whole code path
    /// (ConfigurationFile.CreateDefaultConfigurationFile) through the real command line definition.
    /// </summary>
    [Fact]
    public void Parse_ConfigFileDoesNotExist_WritesDefaultConfigurationFile()
    {
        var configFilePath = Path.Combine(Path.GetTempPath(), $"csharprepl-test-config-{Path.GetRandomFileName()}.rsp");
        Assert.False(File.Exists(configFilePath));

        try
        {
            _ = CommandLine.Parse([], configFilePath);

            Assert.True(File.Exists(configFilePath));
            var contents = File.ReadAllText(configFilePath);

            // header
            Assert.Contains("# Add csharprepl command line options to this file to configure csharprepl.", contents);
            Assert.Contains("# You may uncomment an option below by removing the leading '#' character.", contents);

            // a representative option, with its description and (System.CommandLine v3) "--"-prefixed name
            Assert.Contains("# Reference a shared framework.", contents);
            Assert.Contains("--framework <", contents);

            // ignored options (help/version/configure) should not be written into the file
            Assert.DoesNotContain("--configure <", contents);
        }
        finally
        {
            if (File.Exists(configFilePath)) File.Delete(configFilePath);
        }
    }

    /// <summary>
    /// If the configuration file already exists, Parse must not overwrite it.
    /// </summary>
    [Fact]
    public void Parse_ConfigFileExists_DoesNotOverwrite()
    {
        var configFilePath = Path.Combine(Path.GetTempPath(), $"csharprepl-test-config-{Path.GetRandomFileName()}.rsp");
        File.WriteAllText(configFilePath, "# user customized");

        try
        {
            _ = CommandLine.Parse([], configFilePath);
            Assert.Equal("# user customized", File.ReadAllText(configFilePath));
        }
        finally
        {
            if (File.Exists(configFilePath)) File.Delete(configFilePath);
        }
    }
}
