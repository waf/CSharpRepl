// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Reflection;

namespace Microsoft.CodeAnalysis.Scripting.Hosting;

internal class CommonMemberFilter
{
    public bool Include(MemberInfo member)
    {
        return !IsGeneratedMemberName(member.Name);
    }

    protected virtual bool IsGeneratedMemberName(string name) => false;
}