// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using CSharpRepl.Services.Theming;
using Spectre.Console;

namespace CSharpRepl.Services.Extensions;

internal static class SpectreExtensions
{
    public static Paragraph Append(this Paragraph paragraph, StyledStringSegment text)
        => paragraph.Append(text.Text, text.Style);
}