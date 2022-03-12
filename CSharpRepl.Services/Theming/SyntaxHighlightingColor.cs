// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Diagnostics;
using System.Text.Json.Serialization;

namespace CSharpRepl.Services.Theming;

public readonly struct SyntaxHighlightingColor
{
    [JsonConstructor]
    public SyntaxHighlightingColor(string name, string foreground)
    {
        Debug.Assert(!string.IsNullOrEmpty(name));

        Name = name;
        Foreground = foreground;
    }

    public string Name { get; }
    public string Foreground { get; }
}