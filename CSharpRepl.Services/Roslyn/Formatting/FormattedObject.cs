using System;
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
        if (Value is null) return Array.Empty<FormattedObject>();

        return prettyPrinter.FormatMembers(Value, level.Increment(), includeNonPublic);
    }
}