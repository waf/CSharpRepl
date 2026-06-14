// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Text.RegularExpressions;

namespace CSharpRepl.Tests;

internal static class Extensions
{
    private static readonly Regex AnsiEscapeCodeRegex = new(@"\u001b\[.+?m");

    public static string RemoveFormatting(this string input) => AnsiEscapeCodeRegex.Replace(input, "");
}