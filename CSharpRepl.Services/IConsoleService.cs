// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Threading.Tasks;
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

    /// <summary>
    /// Whether the console is an interactive terminal. False when output is redirected (piped, <c>--eval</c>,
    /// captured by a tool), where cursor movement and live displays (e.g. status spinners) can't render and
    /// must degrade to plain text. Mirrors <c>!Console.IsOutputRedirected</c>; overridable so it can be faked in tests.
    /// </summary>
    bool IsInteractive => !Console.IsOutputRedirected;

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

    /// <summary>Writes a line of Spectre.Console markup. During a live display (e.g. a status spinner) it renders above the live region.</summary>
    void WriteMarkupLine(string markup) => Ansi.MarkupLine(markup);

    /// <summary>
    /// Runs <paramref name="action"/> while displaying an animated status spinner labelled <paramref name="status"/>
    /// (Spectre markup), returning the action's result. Output written to this console during the action - e.g. via
    /// <see cref="WriteMarkupLine"/> - is rendered above the live spinner and remains after it disappears.
    /// </summary>
    Task<T> RunWithStatusAsync<T>(string status, Spinner spinner, string color, Func<Task<T>> action)
        => Ansi.Status().Spinner(spinner).SpinnerStyle(Style.Parse(color)).StartAsync(status, _ => action());

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