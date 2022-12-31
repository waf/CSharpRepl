// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Spectre.Console;

namespace CSharpRepl.Services.Theming;

public readonly struct StyledStringSegment
{
    public readonly string Text;
    public readonly Style? Style;

    public int Length => Text.Length;

    public StyledStringSegment(string text, Style? style = null)
    {
        Text = text;
        Style = style;
    }

    public override string ToString() => Text;

    public static implicit operator StyledStringSegment(string text) => new(text, null);
}