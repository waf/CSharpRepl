// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using CSharpRepl.Services.Theming;
using Microsoft.CodeAnalysis.Scripting.Hosting;

namespace CSharpRepl.Services.Roslyn.CustomObjectFormatters;

internal interface ICustomObjectFormatter
{
    bool IsApplicable(object? value);
    StyledString Format(object? value, int level, CommonObjectFormatter.Visitor visitor);
}

internal abstract class CustomObjectFormatter : ICustomObjectFormatter
{
    public bool IsApplicable(object? value)
    {
        if (value is null) return true;
        return value.GetType().IsAssignableTo(Type);
    }

    public abstract Type Type { get; }

    StyledString ICustomObjectFormatter.Format(object? value, int level, CommonObjectFormatter.Visitor visitor)
    {
        if (value is null) return new StyledString("null", visitor.SyntaxHighlighter.KeywordStyle);
        return Format(value, level, visitor);
    }

    protected abstract StyledString Format(object value, int level, CommonObjectFormatter.Visitor visitor);
}

internal abstract class CustomObjectFormatter<T> : CustomObjectFormatter
    where T : notnull
{
    public override Type Type => typeof(T);

    protected override StyledString Format(object value, int level, CommonObjectFormatter.Visitor visitor)
        => Format((T)value, level, visitor);

    public abstract StyledString Format(T value, int level, CommonObjectFormatter.Visitor visitor);
}