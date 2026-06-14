// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Diagnostics.CodeAnalysis;

namespace CSharpRepl.Services.Extensions;

internal static class MiscExtensions
{
    public static bool TryGet<T>(this T? nullableValue, out T value)
        where T : struct
    {
        if (nullableValue.HasValue)
        {
            value = nullableValue.GetValueOrDefault();
            return true;
        }
        else
        {
            value = default;
            return false;
        }
    }

    public static bool TryGet<T>(this T? nullableValue, [MaybeNullWhen(false)] out T value)
        where T : class
    {
        if (nullableValue is null)
        {
            value = null;
            return false;
        }
        else
        {
            value = nullableValue;
            return true;
        }
    }
}