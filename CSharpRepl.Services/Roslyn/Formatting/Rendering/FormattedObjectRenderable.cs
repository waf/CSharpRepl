// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Collections.Generic;
using Spectre.Console.Rendering;

namespace CSharpRepl.Services.Roslyn.Formatting.Rendering;

internal sealed class FormattedObjectRenderable : IRenderable
{
    private readonly IRenderable renderable;
    private readonly bool renderOnNewLine;

    public FormattedObjectRenderable(IRenderable renderable, bool renderOnNewLine)
    {
        this.renderable = renderable;
        this.renderOnNewLine = renderOnNewLine;
    }

    public Measurement Measure(RenderOptions options, int maxWidth)
        => renderable.Measure(options, maxWidth);

    public IEnumerable<Segment> Render(RenderOptions options, int maxWidth)
    {
        if (renderOnNewLine)
        {
            yield return Segment.LineBreak;
        }
        foreach (var segment in renderable.Render(options, maxWidth))
        {
            yield return segment;
        }
    }
}