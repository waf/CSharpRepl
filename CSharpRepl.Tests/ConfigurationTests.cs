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
}
