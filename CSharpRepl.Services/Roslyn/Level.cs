// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;

namespace CSharpRepl.Services.Roslyn;

public enum Level
{
    FirstDetailed,
    FirstSimple,
    Second,
    ThirdPlus
}

internal static class LevelX
{
    public static Level Increment(this Level level) => (Level)Math.Min((int)level + 1, (int)Level.ThirdPlus);
}