// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using CSharpRepl.Services.Theming;

namespace CSharpRepl.Services.Roslyn.CustomObjectFormatters;

internal class TypeFormatter : CustomObjectFormatter<Type>
{
    public static readonly TypeFormatter Instance = new();

    public override bool IsFormattingExhaustive => false;

    private TypeFormatter() { }

    public override StyledString Format(Type value, Level level, Formatter formatter)
    {
        return formatter.FormatTypeName(
            value,
            showNamespaces: level == Level.FirstDetailed,
            useLanguageKeywords: level != Level.FirstDetailed);
    }
}