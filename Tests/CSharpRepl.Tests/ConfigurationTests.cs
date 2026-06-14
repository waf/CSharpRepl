// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using CSharpRepl.Services;
using Xunit;

namespace CSharpRepl.Tests;

public class ConfigurationTests
{
    [Theory]
    [InlineData("Enter", ConsoleKey.Enter, '\n', default(ConsoleModifiers))]
    [InlineData("Control+Enter", ConsoleKey.Enter, '\n', ConsoleModifiers.Control)]
    [InlineData("Control+Alt+Enter", ConsoleKey.Enter, '\n', ConsoleModifiers.Control | ConsoleModifiers.Alt)]
    [InlineData("ctrl+enter", ConsoleKey.Enter, '\n', ConsoleModifiers.Control)]
    [InlineData("A", ConsoleKey.A, 'A', default(ConsoleModifiers))]
    [InlineData("a", ConsoleKey.A, 'A', default(ConsoleModifiers))]
    [InlineData("Alt+Shift+A", ConsoleKey.A, 'A', ConsoleModifiers.Alt | ConsoleModifiers.Shift)]
    public void ParseKeyPressPattern_Key(string pattern, ConsoleKey expectedKey, char expectedChar, ConsoleModifiers expectedModifiers)
    {
        var parsed = Configuration.ParseKeyPressPattern(pattern);
        Assert.Equal(expectedKey, parsed.Key);
        Assert.Equal(expectedChar, parsed.Character);
        Assert.Equal(expectedModifiers, parsed.Modifiers);
    }

    [Theory]
    [InlineData("(", '(')]
    [InlineData(".", '.')]
    [InlineData(" ", ' ')]
    public void ParseKeyPressPattern_KeyChar(string pattern, char keyChar)
    {
        var parsed = Configuration.ParseKeyPressPattern(pattern);
        Assert.Equal(default, parsed.Key);
        Assert.Equal(keyChar, parsed.Character);
        Assert.Equal(default, parsed.Modifiers);
    }

    [Fact]
    public void GetApplicationDirectory_WindowsRoamingExists_KeepsRoaming()
    {
        // Existing Windows installations keep their (roaming) directory so config and caches aren't orphaned.
        var result = Configuration.GetApplicationDirectory("roaming", "local", directoryExists: dir => dir == "roaming", isWindows: true);
        Assert.Equal("roaming", result);
    }

    [Fact]
    public void GetApplicationDirectory_WindowsRoamingMissing_UsesLocal()
    {
        // New Windows installations prefer the local directory so the package cache isn't synced across machines.
        var result = Configuration.GetApplicationDirectory("roaming", "local", directoryExists: _ => false, isWindows: true);
        Assert.Equal("local", result);
    }

    [Fact]
    public void GetApplicationDirectory_NonWindows_AlwaysUsesRoaming()
    {
        // The roaming-profile sync problem only exists on Windows, so Unix behavior is left unchanged
        // (ApplicationData), regardless of whether that directory already exists.
        Assert.Equal("roaming", Configuration.GetApplicationDirectory("roaming", "local", directoryExists: _ => false, isWindows: false));
        Assert.Equal("roaming", Configuration.GetApplicationDirectory("roaming", "local", directoryExists: _ => true, isWindows: false));
    }
}
