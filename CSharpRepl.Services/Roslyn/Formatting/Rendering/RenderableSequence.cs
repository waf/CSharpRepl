// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Collections.Generic;
using Spectre.Console.Rendering;

namespace CSharpRepl.Services.Roslyn.Formatting.Rendering;

internal sealed class RenderableSequence : Renderable
{
    private readonly Wrap[] items;

    public RenderableSequence(IRenderable r1, IRenderable r2, bool separateByLineBreak)
    {
        items = new Wrap[] { new(r1, separateByLineBreak), new(r2, false) };
    }

    protected override IEnumerable<Segment> Render(RenderOptions options, int maxWidth)
    {
        foreach (var item in items)
        {
            foreach (var segment in item.Renderable.Render(options, maxWidth))
            {
                yield return segment;
            }

            if (item.UseLineBreak)
            {
                yield return Segment.LineBreak;
            }
        }
    }

    private readonly struct Wrap
    {
        public readonly IRenderable Renderable;
        public readonly bool UseLineBreak;

        public Wrap(IRenderable renderable, bool useLineBreak)
        {
            Renderable = renderable;
            UseLineBreak = useLineBreak;
        }
    }
}