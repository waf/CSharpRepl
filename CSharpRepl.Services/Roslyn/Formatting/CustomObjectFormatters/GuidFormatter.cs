// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using CSharpRepl.Services.Theming;
using Microsoft.CodeAnalysis.Classification;

namespace CSharpRepl.Services.Roslyn.Formatting.CustomObjectFormatters;

internal sealed class GuidFormatter : CustomObjectFormatter<Guid>
{
    public static readonly GuidFormatter Instance = new();

    private GuidFormatter() { }

    public override StyledString FormatToText(Guid value, Level level, Formatter formatter)
    {
        //32 digits separated by hyphens: 00000000-0000-0000-0000-000000000000
        var parts = value.ToString().Split('-');
        var style = formatter.GetStyle(ClassificationTypeNames.NumericLiteral);
        return new StyledString(
            new StyledStringSegment[]
            {
                new(parts[0], style),
                new("-"),
                new(parts[1], style),
                new("-"),
                new(parts[2], style),
                new("-"),
                new(parts[3], style),
                new("-"),
                new(parts[4], style)
            });
    }
}