// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Microsoft.CodeAnalysis;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace CSharpRepl.Services.Roslyn.References
{
    /// <summary>
    /// Compares assembly references based on their filepath.
    /// </summary>
    internal sealed class AssemblyReferenceComparer : IEqualityComparer<MetadataReference>
    {
        public bool Equals(MetadataReference? x, MetadataReference? y) =>
            x?.Display == y?.Display;

        public int GetHashCode([DisallowNull] MetadataReference obj) =>
            (obj.Display ?? string.Empty).GetHashCode();
    }
}
