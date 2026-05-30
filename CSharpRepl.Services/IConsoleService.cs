// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using PrettyPrompt.Consoles;
using PrettyPrompt.Highlighting;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace CSharpRepl.Services;

/// <summary>
/// CSharpRepl's console abstraction. Wraps the Spectre and PrettyPrompt consoles.
/// </summary>
public interface IConsoleService
{
    // The underlying PrettyPrompt console that's used for the interactive prompt input.
    protected IConsole PrettyPromptConsole { get; }

    // The underlying Spectre console that provides e.g. color coded / wrapped output.
    protected IAnsiConsole Ansi { get; }

    /// <summary>Width, in characters, of the console buffer — for layout/wrapping math.</summary>
    int BufferWidth => PrettyPromptConsole.BufferWidth;

    /// <summary>Rendering profile (capabilities + width) of the underlying console.</summary>
    Profile Profile => Ansi.Profile;

    /// <summary>Cursor control for the underlying console.</summary>
    IAnsiConsoleCursor Cursor => Ansi.Cursor;

    /// <summary>Clears the screen.</summary>
    void Clear() => Ansi.Clear(home: true);

    void Write(IRenderable renderable) => Ansi.Write(renderable);
    void Write(string text) => Ansi.Write(text);
    void Write(FormattedString text) => PrettyPromptConsole.Write(text);

    void WriteLine(string text) => Ansi.WriteLine(text);
    void WriteLine() => Ansi.WriteLine();
    void WriteLine(FormattedString text) => PrettyPromptConsole.WriteLine(text);

    /// <summary>
    /// Writes a line of plain, unwrapped text to standard output. Use this for non-interactive output (e.g. --eval / piped results,
    /// redirected output). Different from <see cref="WriteLine(string)"/> which writes via Spectre's AnsiConsole and word-wraps to
    /// the console width (corrupting a value meant for piping).
    /// </summary>
    void WriteStandardOutputLine(string text) => PrettyPromptConsole.WriteLine(text);

    /// <summary>
    /// Similar to <see cref="WriteStandardOutputLine(string)"/> but for standard error.
    /// </summary>
    void WriteStandardErrorLine(string text) => PrettyPromptConsole.WriteErrorLine(text);

    void WriteError(string text)
    {
        if (PrettyPromptConsole.IsErrorRedirected)
        {
            PrettyPromptConsole.WriteError(text);
        }
        else
        {
            //AnsiConsole is smarter about word wrapping
            Write(text);
        }
    }

    /// <param name="text">Text written to error stream (used only if error stream is redirected).</param>
    void WriteError(IRenderable renderable, string text)
    {
        if (PrettyPromptConsole.IsErrorRedirected)
        {
            PrettyPromptConsole.WriteError(text);
        }
        else
        {
            //AnsiConsole is smarter about word wrapping
            Write(renderable);
        }
    }

    /// <param name="text">Text written to error stream (used only if error stream is redirected).</param>
    void WriteErrorLine(IRenderable renderable, string text)
    {
        if (PrettyPromptConsole.IsErrorRedirected)
        {
            PrettyPromptConsole.WriteErrorLine(text);
        }
        else
        {
            //AnsiConsole is smarter about word wrapping
            Write(renderable);
            WriteLine();
        }
    }

    void WriteErrorLine(string text)
    {
        if (PrettyPromptConsole.IsErrorRedirected)
        {
            PrettyPromptConsole.WriteErrorLine(text);
        }
        else
        {
            //AnsiConsole is smarter about word wrapping
            WriteLine(text);
        }
    }

    string? ReadLine();
}