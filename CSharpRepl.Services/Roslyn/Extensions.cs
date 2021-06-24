// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Microsoft.CodeAnalysis;
using System;
using System.Collections.Generic;
using System.Linq;

namespace CSharpRepl.Services.Roslyn
{
    internal static class Extensions
    {
        public static Solution ApplyChanges(this Solution edit, Workspace workspace)
        {
            if (!workspace.TryApplyChanges(edit))
            {
                throw new InvalidOperationException("Failed to apply edit to workspace");
            }
            return workspace.CurrentSolution;
        }

        // purely for nullable reference type analysis
        public static IEnumerable<T> WhereNotNull<T>(this IEnumerable<T?> source) where T : class
        {
            return source.Where(x => x != null)!;
        }
    }
}
