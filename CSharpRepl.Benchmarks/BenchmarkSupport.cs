// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.IO;
using CSharpRepl.Services;
using CSharpRepl.Services.Logging;
using PrettyPrompt.Consoles;
using Spectre.Console;

namespace CSharpRepl.Benchmarks;

/// <summary>
/// Minimal <see cref="IConsoleService"/> for benchmarks. The per-keystroke Roslyn paths
/// (highlight / complete / format) only touch the console on initialization or warm-up errors,
/// plus the rendering <see cref="Profile"/>, so every sink here is a no-op.
/// </summary>
internal sealed class BenchmarkConsole : IConsoleService
{
    private readonly IConsole prettyPromptConsole = new NullPromptConsole();
    private readonly IAnsiConsole ansi = AnsiConsole.Create(new AnsiConsoleSettings
    {
        Ansi = AnsiSupport.No,
        ColorSystem = ColorSystemSupport.NoColors,
        Out = new AnsiConsoleOutput(TextWriter.Null),
    });

    IConsole IConsoleService.PrettyPromptConsole => prettyPromptConsole;
    IAnsiConsole IConsoleService.Ansi => ansi;
    public string? ReadLine() => null;

    private sealed class NullPromptConsole : IConsole
    {
        public int CursorTop => 0;
        public int BufferWidth => 240;
        public int WindowHeight => 80;
        public int WindowTop => 0;
        public bool KeyAvailable => false;
        public bool CaptureControlC { get => false; set { } }
        public bool IsErrorRedirected => false;
        public event ConsoleCancelEventHandler CancelKeyPress { add { } remove { } }
        public void Clear() { }
        public void HideCursor() { }
        public void InitVirtualTerminalProcessing() { }
        public ConsoleKeyInfo ReadKey(bool intercept) => default;
        public void ShowCursor() { }
        public void Write(string? value) { }
        public void WriteError(string? value) { }
        public void WriteErrorLine(string? value) { }
        public void WriteLine(string? value) { }
        public void Write(ReadOnlySpan<char> value) { }
        public void WriteError(ReadOnlySpan<char> value) { }
        public void WriteErrorLine(ReadOnlySpan<char> value) { }
        public void WriteLine(ReadOnlySpan<char> value) { }
    }
}

/// <summary>No-op trace logger; the real one only matters with the --trace flag.</summary>
internal sealed class NullTraceLogger : ITraceLogger
{
    public void Log(string message) { }
    public void Log(Func<string> message) { }
    public void LogPaths(string message, Func<IEnumerable<string?>> paths) { }
}
