// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using PrettyPrompt.Consoles;
using Spectre.Console;

namespace CSharpRepl.Services;

public sealed class SystemConsoleEx : IConsoleEx
{
    private readonly IAnsiConsole ansiConsole = AnsiConsole.Console;

    public IConsole PrettyPromptConsole { get; } = new SystemConsole();
    IConsole IConsoleEx.PrettyPromptConsole => PrettyPromptConsole;
    IAnsiConsole IConsoleEx.Ansi => ansiConsole;

    public string? ReadLine() => Console.ReadLine();
}