// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Diagnostics.CodeAnalysis;

namespace Microsoft.CodeAnalysis.CSharp.Symbols;

internal static class GeneratedNameParser
{
    internal static bool IsSynthesizedLocalName(string name)
        => name.StartsWith(GeneratedNameConstants.SynthesizedLocalNamePrefix, StringComparison.Ordinal);

    // The type of generated name. See TryParseGeneratedName.
    internal static GeneratedNameKind GetKind(string name)
        => TryParseGeneratedName(name, out var kind, out _, out _) ? kind : GeneratedNameKind.None;

    // Parse the generated name. Returns true for names of the form
    // [CS$]<[middle]>c[__[suffix]] where [CS$] is included for certain
    // generated names, where [middle] and [__[suffix]] are optional,
    // and where c is a single character in [1-9a-z]
    // (csharp\LanguageAnalysis\LIB\SpecialName.cpp).
    internal static bool TryParseGeneratedName(
        string name,
        out GeneratedNameKind kind,
        out int openBracketOffset,
        out int closeBracketOffset)
    {
        openBracketOffset = -1;
        if (name.StartsWith("CS$<", StringComparison.Ordinal))
        {
            openBracketOffset = 3;
        }
        else if (name.StartsWith("<", StringComparison.Ordinal))
        {
            openBracketOffset = 0;
        }

        if (openBracketOffset >= 0)
        {
            closeBracketOffset = IndexOfBalancedParenthesis(name, openBracketOffset, '>');
            if (closeBracketOffset >= 0 && closeBracketOffset + 1 < name.Length)
            {
                int c = name[closeBracketOffset + 1];
                if (c is >= '1' and <= '9' or >= 'a' and <= 'z') // Note '0' is not special.
                {
                    kind = (GeneratedNameKind)c;
                    return true;
                }
            }
        }

        kind = GeneratedNameKind.None;
        openBracketOffset = -1;
        closeBracketOffset = -1;
        return false;
    }

    private static int IndexOfBalancedParenthesis(string str, int openingOffset, char closing)
    {
        char opening = str[openingOffset];

        int depth = 1;
        for (int i = openingOffset + 1; i < str.Length; i++)
        {
            var c = str[i];
            if (c == opening)
            {
                depth++;
            }
            else if (c == closing)
            {
                depth--;
                if (depth == 0)
                {
                    return i;
                }
            }
        }

        return -1;
    }

    internal static bool TryParseSourceMethodNameFromGeneratedName(string generatedName, GeneratedNameKind requiredKind, [NotNullWhen(true)] out string? methodName)
    {
        if (!TryParseGeneratedName(generatedName, out var kind, out int openBracketOffset, out int closeBracketOffset))
        {
            methodName = null;
            return false;
        }

        if (requiredKind != 0 && kind != requiredKind)
        {
            methodName = null;
            return false;
        }

        methodName = generatedName.Substring(openBracketOffset + 1, closeBracketOffset - openBracketOffset - 1);

        if (kind.IsTypeName())
        {
            methodName = methodName.Replace(GeneratedNameConstants.DotReplacementInTypeNames, '.');
        }

        return true;
    }
}