// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using CSharpRepl.Services.Theming;
using Spectre.Console;

namespace CSharpRepl.Services.Roslyn.Formatting;

public static class LengthLimiting
{
    public static int GetMaxParagraphLength(Level level, Profile profile)
    {
        var ratio = level switch
        {
            Level.FirstDetailed => 2,
            Level.FirstSimple => 0.4,
            Level.Second => 0.2,
            Level.ThirdPlus => 0.1,
            _ => throw new InvalidOperationException("unexpected level")
        };

        return (int)(ratio * profile.Width * profile.Height);
    }

    public static int GetTableMaxItems(Level level, Profile profile)
    {
        var ratio = level switch
        {
            Level.FirstDetailed => 2,
            Level.FirstSimple => 0.5,
            Level.Second => 0.4,
            Level.ThirdPlus => 0.3,
            _ => throw new InvalidOperationException("unexpected level")
        };

        return (int)(ratio * profile.Height);
    }

    public static int GetTreeMaxItems(Level level, Profile profile)
    {
        var ratio = level switch
        {
            Level.FirstDetailed => 2,
            Level.FirstSimple => 0.5,
            Level.Second => 0.4,
            Level.ThirdPlus => 0.3,
            _ => throw new InvalidOperationException("unexpected level")
        };

        return (int)(ratio * profile.Height);
    }

    public static string LimitLength(string? value, Level level, Profile profile)
    {
        if (string.IsNullOrEmpty(value)) return "";
        var maxLen = GetMaxParagraphLength(level, profile);
        if (value.Length > maxLen)
        {
            value = string.Concat(value.AsSpan(0, maxLen), "...");
        }
        return value;
    }

    public static StyledStringSegment LimitLength(StyledStringSegment value, Level level, Profile profile)
    {
        var maxLen = GetMaxParagraphLength(level, profile);
        if (value.Length > maxLen)
        {
            value = value.Substring(0, maxLen) + "...";
        }
        return value;
    }

    public static StyledString LimitLength(StyledString value, Level level, Profile profile)
    {
        var maxLen = GetMaxParagraphLength(level, profile);
        if (value.Length > maxLen)
        {
            value = value.Substring(0, maxLen) + "...";
        }
        return value;
    }
}