// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Runtime.CompilerServices;
using CSharpRepl.Services.Theming;

namespace CSharpRepl.Services.Roslyn.Formatting.CustomObjectFormatters;

internal class TupleFormatter : CustomObjectFormatter<ITuple>
{
    public static readonly TupleFormatter Instance = new();

    public override bool IsFormattingExhaustive => true;

    private TupleFormatter() { }

    public override StyledString Format(ITuple value, Level level, Formatter formatter)
    {
        var sb = new StyledStringBuilder();

        sb.Append('(');
        for (int i = 0; i < value.Length; i++)
        {
            sb.Append(formatter.FormatObjectToText(value[i], level));

            bool isLast = i == value.Length - 1;
            if (!isLast) sb.Append(", ");
        }
        sb.Append(')');

        return sb.ToStyledString();
    }
}