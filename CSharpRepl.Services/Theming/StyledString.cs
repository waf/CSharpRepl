// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Collections.Generic;
using System.Diagnostics;
using CSharpRepl.Services.Extensions;
using PrettyPrompt.Documents;
using Spectre.Console;

namespace CSharpRepl.Services.Theming;

public readonly struct StyledString
{
    public static readonly StyledString Empty = new("");

    private readonly List<StyledStringSegment> parts;

    public int Length { get; }
    public bool IsEmpty => parts.Count == 0;
    public IReadOnlyList<StyledStringSegment> Parts => parts;

    public StyledString(StyledStringSegment part)
    {
        if (part.Length > 0)
        {
            parts = new List<StyledStringSegment>(1) { part };
            Length = part.Length;
        }
        else
        {
            parts = Empty.parts ?? new(0);
        }
    }

    public StyledString(string part, Style? style = null)
        : this(new StyledStringSegment(part, style))
    { }

    public StyledString(IEnumerable<StyledStringSegment> parts)
    {
        this.parts = new List<StyledStringSegment>();
        int len = 0;
        foreach (var part in parts)
        {
            if (part.Length > 0)
            {
                this.parts.Add(part);
                len += part.Length;
            }
        }
        Length = len;
    }

    public char FirstChar => parts[0].Text[0];
    public char LastChar => parts[^1].Text[0];

    /// <summary>
    /// Retrieves a substring from this instance. The substring starts at a specified character position and has a specified length.
    /// </summary>
    public StyledString Substring(int startIndex, int length)
    {
        //formal argument validation will be done in Text.Substring(...)
        Debug.Assert(startIndex >= 0 && startIndex <= Length);
        Debug.Assert(length >= 0 && length - startIndex <= Length);

        if (IsEmpty || length == 0) return Empty;
        if (length - startIndex == Length) return this;

        var resultParts = new List<StyledStringSegment>(parts.Count);
        int i = 0;
        foreach (var part in parts)
        {
            var partSpan = new TextSpan(i, part.Length);
            if (partSpan.Overlap(startIndex, length).TryGet(out var newSpan))
            {
                resultParts.Add(new StyledStringSegment(part.Text.Substring(newSpan.Start - i, newSpan.Length), part.Style));
            }
            i += part.Length;
        }

        return new StyledString(resultParts);
    }

    public Paragraph ToParagraph()
    {
        var p = new Paragraph();
        foreach (var part in parts)
        {
            p.Append(part.Text, part.Style);
        }
        return p;
    }

    public override string ToString() => string.Join("", parts);

    public static implicit operator StyledString(string text) => new(new StyledStringSegment(text));
    public static implicit operator StyledString(StyledStringSegment text) => new(text);
}