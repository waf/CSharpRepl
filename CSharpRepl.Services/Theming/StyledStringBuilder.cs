// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using Spectre.Console;

namespace CSharpRepl.Services.Theming;

public sealed class StyledStringBuilder
{
    private readonly List<StyledStringSegment> parts = new();

    private int length;
    public int Length => length;

    public StyledStringBuilder()
    { }

    public StyledStringBuilder(StyledStringSegment text)
    {
        Add(text);
    }

    public StyledStringBuilder(string? text, Style? style)
    {
        if (text != null)
        {
            Add(new StyledStringSegment(text, style));
        }
    }

    //string has implicit conversion to Style which is not desirable here
    [Obsolete("Do not use this overload. You need to pass Style object and not string implicitly parsed to Style.")]
    public StyledStringBuilder(string? text, string style)
        => throw new NotSupportedException();

    public StyledStringBuilder Append(string? text, Style? style = null)
    {
        if (text != null)
        {
            Add(new StyledStringSegment(text, style));
        }
        return this;
    }

    //string has implicit conversion to Style which is not desirable here
    [Obsolete("Do not use this overload. You need to pass Style object and not string implicitly parsed to Style.")]
    public StyledStringBuilder Append(string? text, string style)
        => throw new NotSupportedException();

    public StyledStringBuilder Append(char c, Style? style = null)
    {
        Add(new StyledStringSegment(c.ToString(), style));
        return this;
    }

    public StyledStringBuilder Append(StyledString text)
    {
        foreach (var part in text.Parts)
        {
            Add(part);
        }
        return this;
    }

    public StyledStringBuilder Append(StyledStringSegment text)
    {
        Add(text);
        return this;
    }

    private void Add(StyledStringSegment text)
    {
        parts.Add(text);
        length += text.Length;
    }

    public override string ToString() => string.Join("", parts);
    public StyledString ToStyledString() => new(parts);

    public void Clear()
    {
        parts.Clear();
        length = 0;
    }
}