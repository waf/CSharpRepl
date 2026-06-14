// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Collections.Generic;
using Spectre.Console.Rendering;

namespace CSharpRepl.Services.Roslyn.Formatting;

internal readonly struct FormattedObject
{
    public readonly IRenderable Renderable;
    public readonly object? Value;

    public FormattedObject(IRenderable renderable, object? value)
    {
        Renderable = renderable;
        Value = value;
    }

    public IEnumerable<FormattedObject> FormatMembers(PrettyPrinter prettyPrinter, Level level, bool includeNonPublic)
    {
        if (Value is null) return [];

        return prettyPrinter.FormatMembers(Value, level.Increment(), includeNonPublic);
    }
}