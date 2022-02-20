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
    [InlineData("Enter", ConsoleKey.Enter, default(ConsoleModifiers))]
    [InlineData("Control+Enter", ConsoleKey.Enter, ConsoleModifiers.Control)]
    [InlineData("Control+Alt+Enter", ConsoleKey.Enter, ConsoleModifiers.Control | ConsoleModifiers.Alt)]
    [InlineData("ctrl+enter", ConsoleKey.Enter, ConsoleModifiers.Control)]
    [InlineData("A", ConsoleKey.A, default(ConsoleModifiers))]
    [InlineData("a", ConsoleKey.A, default(ConsoleModifiers))]
    [InlineData("Alt+Shift+A", ConsoleKey.A, ConsoleModifiers.Alt | ConsoleModifiers.Shift)]
    public void ParseKeyPressPattern_Key(string pattern, ConsoleKey key, ConsoleModifiers modifiers)
    {
        var parsed = Configuration.ParseKeyPressPattern(pattern);
        Assert.Equal(key, parsed.Key);
        Assert.Equal(default, parsed.Character);
        Assert.Equal(modifiers, parsed.Modifiers);
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
}
