// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using CSharpRepl.Services.Theming;
using Microsoft.CodeAnalysis.Scripting.Hosting;

namespace CSharpRepl.Services.Roslyn.CustomObjectFormatters;

internal class TypeFormatter : CustomObjectFormatter<Type>
{
    public static readonly TypeFormatter Instance = new(forceUsageOfLanguageKeywords: false);
    public static readonly TypeFormatter InstanceWithForcedUsageOfLanguageKeywords = new(forceUsageOfLanguageKeywords: true);

    private readonly bool forceUsageOfLanguageKeywords;

    private TypeFormatter(bool forceUsageOfLanguageKeywords)
    {
        this.forceUsageOfLanguageKeywords = forceUsageOfLanguageKeywords;
    }

    public override StyledString Format(Type value, int level, CommonObjectFormatter.Visitor visitor)
    {
        return visitor.TypeNameFormatter.FormatTypeName(
            value,
            new CommonTypeNameFormatterOptions(
                arrayBoundRadix: visitor.TypeNameOptions.ArrayBoundRadix,
                showNamespaces: level == 0,
                useLanguageKeywords: forceUsageOfLanguageKeywords || level > 0));
    }
}