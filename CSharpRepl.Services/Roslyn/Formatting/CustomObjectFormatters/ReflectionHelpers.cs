// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Collections.Generic;
using System.Reflection;

namespace CSharpRepl.Services.Roslyn.Formatting.CustomObjectFormatters;

internal static class ReflectionHelpers
{
    public static IEnumerable<string> GetModifiers(MethodInfo methodInfo)
    {
        if (methodInfo.IsPrivate) yield return "private";
        else if (methodInfo.IsFamily) yield return "protected";
        else if (methodInfo.IsFamilyOrAssembly) yield return "protected internal";
        else if (methodInfo.IsFamilyAndAssembly) yield return "private protected";
        else if (methodInfo.IsAssembly) yield return "internal";
        else if (methodInfo.IsPublic) yield return "public";

        if (methodInfo.IsStatic) yield return "static";

        if (methodInfo.IsAbstract) yield return "abstract";
        else if (methodInfo.IsVirtual) yield return "virtual";
    }
}