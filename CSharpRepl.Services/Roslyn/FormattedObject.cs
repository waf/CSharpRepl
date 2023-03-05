using System;
using System.Collections.Generic;
using CSharpRepl.Services.Roslyn.CustomObjectFormatters;
using Spectre.Console.Rendering;

namespace CSharpRepl.Services.Roslyn;

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