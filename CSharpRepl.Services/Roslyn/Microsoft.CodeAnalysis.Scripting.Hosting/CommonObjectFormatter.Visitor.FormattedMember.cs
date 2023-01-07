// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

#nullable disable

using System.Diagnostics;
using CSharpRepl.Services.Theming;

namespace Microsoft.CodeAnalysis.Scripting.Hosting;

internal abstract partial class CommonObjectFormatter
{
    internal sealed partial class Visitor
    {
        private readonly struct FormattedMember
        {
            // Non-negative if the member is an inlined element of an array (DebuggerBrowsableState.RootHidden applied on a member of array type).
            public readonly int Index;

            // Formatted name of the member or null if it doesn't have a name (Index is >=0 then).
            public readonly StyledString Name;

            // Formatted value of the member.
            public readonly StyledString Value;

            public FormattedMember(int index, StyledString name, StyledString value)
            {
                Debug.Assert(!name.IsEmpty || (index >= 0));
                Name = name;
                Index = index;
                Value = value;
            }

            /// <remarks>
            /// Doesn't (and doesn't need to) reflect the number of digits in <see cref="Index"/> since
            /// it's only used for a conservative approximation (shorter is more conservative when trying
            /// to determine the minimum number of members that will fill the output).
            /// </remarks>
            public int MinimalLength => (!Name.IsEmpty ? Name.Length : "[0]".Length) + Value.Length;

            public StyledString GetDisplayName()
            {
                return Name.IsEmpty ? "[" + Index.ToString() + "]" : Name;
            }

            public bool HasKeyName()
            {
                return Index >= 0 && !Name.IsEmpty && Name.Length >= 2 && Name.FirstChar == '[' && Name.LastChar == ']';
            }

            public bool AppendAsCollectionEntry(Builder result)
            {
                // Some BCL collections use [{key.ToString()}]: {value.ToString()} pattern to display collection entries.
                // We want them to be printed initializer-style, i.e. { <key>, <value> } 
                if (HasKeyName())
                {
                    result.AppendGroupOpening();
                    result.AppendCollectionItemSeparator(isFirst: true, inline: true);
                    result.Append(Name, 1, Name.Length - 2);
                    result.AppendCollectionItemSeparator(isFirst: false, inline: true);
                    result.Append(Value);
                    result.AppendGroupClosing(inline: true);
                }
                else
                {
                    result.Append(Value);
                }

                return true;
            }

            public bool Append(Builder result, string separator)
            {
                result.Append(GetDisplayName());
                result.Append(separator);
                result.Append(Value);
                return true;
            }
        }
    }
}