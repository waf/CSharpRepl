// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using PrettyPrompt.Consoles;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace CSharpRepl.Services;

public interface IConsoleEx : IConsole, IAnsiConsole
{
    /// <param name="text">Text written to error stream (used only if error stream is redirected).</param>
    void WriteError(IRenderable renderable, string text);
}