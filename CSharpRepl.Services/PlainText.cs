// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Collections.Generic;
using Spectre.Console.Rendering;

namespace CSharpRepl.Services;

/// <summary>
/// An <see cref="IRenderable"/> that emits its text verbatim — ignoring the console width, so no word-wrapping,
/// cropping, or markup parsing. Use for machine-consumable output (e.g. the <c>inspect init</c> shell exports)
/// where wrapping a long path would corrupt a copy-paste or a pipe. Contrast with <see cref="Spectre.Console.Text"/>,
/// whose only overflow modes (Fold/Crop/Ellipsis) are all width-driven.
/// </summary>
public sealed class PlainText(string text) : Renderable
{
    protected override IEnumerable<Segment> Render(RenderOptions options, int maxWidth)
    {
        // Split on '\n' and ignore maxWidth entirely: each line becomes a single segment, so the text is
        // written exactly as given. A trailing '\r' is stripped so CRLF input doesn't emit a stray control char.
        var lines = text.Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i].TrimEnd('\r');
            if (line.Length > 0)
            {
                yield return new Segment(line);
            }

            // Line break between lines only — the caller appends the final newline (Write + WriteLine).
            if (i < lines.Length - 1)
            {
                yield return Segment.LineBreak;
            }
        }
    }
}
