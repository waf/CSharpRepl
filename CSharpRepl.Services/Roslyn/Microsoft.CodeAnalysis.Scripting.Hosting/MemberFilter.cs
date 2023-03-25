// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Reflection;
using Microsoft.CodeAnalysis.CSharp.Symbols;

namespace Microsoft.CodeAnalysis.Scripting.Hosting;

internal sealed class MemberFilter
{
    public bool Include(MemberInfo member)
        => !IsGeneratedMemberName(member.Name);

    private bool IsGeneratedMemberName(string name)
    {
        // Generated fields, e.g. "<property_name>k__BackingField"
        return GeneratedNames.IsGeneratedMemberName(name);
    }
}