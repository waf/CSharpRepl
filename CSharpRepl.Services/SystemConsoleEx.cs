// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using PrettyPrompt.Consoles;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace CSharpRepl.Services;

public sealed class SystemConsoleEx : SystemConsole, IConsoleEx
{
    private readonly IAnsiConsole ansiConsole;

    public SystemConsoleEx()
    {
        ansiConsole = AnsiConsole.Console;
    }

    public void WriteError(IRenderable renderable, string text)
    {
        if (IsErrorRedirected)
        {
            WriteError(text);
        }
        else
        {
            Write(renderable);
        }
    }

    public Profile Profile => ansiConsole.Profile;
    public IAnsiConsoleCursor Cursor => ansiConsole.Cursor;
    public IAnsiConsoleInput Input => ansiConsole.Input;
    public IExclusivityMode ExclusivityMode => ansiConsole.ExclusivityMode;
    public RenderPipeline Pipeline => ansiConsole.Pipeline;
    public void Clear(bool home) => ansiConsole.Clear(home);
    public void Write(IRenderable renderable) => ansiConsole.Write(renderable);
}