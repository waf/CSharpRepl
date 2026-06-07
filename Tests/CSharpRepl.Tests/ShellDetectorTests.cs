// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using CSharpRepl.Commands;
using Xunit;

namespace CSharpRepl.Tests;

public class ShellDetectorTests
{
    [Theory]
    [InlineData("pwsh", "pwsh")]
    [InlineData("powershell", "pwsh")]
    [InlineData("PowerShell", "pwsh")] // casing-insensitive (Windows ProcessName casing varies)
    [InlineData("cmd", "cmd")]
    [InlineData("bash", "bash")]
    [InlineData("sh", "bash")]
    [InlineData("zsh", "bash")]
    [InlineData("dash", "bash")]
    [InlineData("ksh", "bash")]
    [InlineData("fish", "fish")]
    public void MapShellName_RecognizedShells_NormalizesToSyntaxFamily(string processName, string expected)
        => Assert.Equal(expected, ShellDetector.MapShellName(processName));

    [Theory]
    [InlineData("dotnet")]
    [InlineData("csharprepl")]
    [InlineData("explorer")]
    [InlineData("code")]
    [InlineData("")]
    public void MapShellName_NonShells_ReturnsNull(string processName)
        => Assert.Null(ShellDetector.MapShellName(processName));

    [Fact]
    public void DetectShell_IsBestEffortAndNeverThrows()
    {
        // The result depends on the test host's process tree, so we only assert it doesn't throw and
        // returns either null or a normalized name the export builder understands.
        var shell = ShellDetector.DetectShell();
        Assert.True(shell is null or "pwsh" or "cmd" or "bash" or "fish");
    }
}
