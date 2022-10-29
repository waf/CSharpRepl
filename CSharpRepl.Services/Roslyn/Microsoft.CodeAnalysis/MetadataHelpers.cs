// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

#nullable disable

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Reflection.Metadata;
using System.Text;
using Microsoft.CodeAnalysis.Collections;
using Microsoft.CodeAnalysis.PooledObjects;
using Roslyn.Utilities;

namespace Microsoft.CodeAnalysis;

internal static class MetadataHelpers
{
    public const char DotDelimiter = '.';
    public const string DotDelimiterString = ".";
    public const char GenericTypeNameManglingChar = '`';
    private const string GenericTypeNameManglingString = "`";
    public const int MaxStringLengthForParamSize = 22;
    public const int MaxStringLengthForIntToStringConversion = 22;
    public const string SystemString = "System";

    // These can appear in the interface name that precedes an explicit interface implementation member.
    public const char MangledNameRegionStartChar = '<';
    public const char MangledNameRegionEndChar = '>';

    internal readonly struct AssemblyQualifiedTypeName
    {
        internal readonly string TopLevelType;
        internal readonly string[] NestedTypes;
        internal readonly AssemblyQualifiedTypeName[] TypeArguments;
        internal readonly int PointerCount;

        /// <summary>
        /// Rank equal 0 is used to denote an SzArray, rank equal 1 denotes multi-dimensional array of rank 1.
        /// </summary>
        internal readonly int[] ArrayRanks;
        internal readonly string AssemblyName;

        internal AssemblyQualifiedTypeName(
            string topLevelType,
            string[] nestedTypes,
            AssemblyQualifiedTypeName[] typeArguments,
            int pointerCount,
            int[] arrayRanks,
            string assemblyName)
        {
            this.TopLevelType = topLevelType;
            this.NestedTypes = nestedTypes;
            this.TypeArguments = typeArguments;
            this.PointerCount = pointerCount;
            this.ArrayRanks = arrayRanks;
            this.AssemblyName = assemblyName;
        }
    }

    private static readonly string[] s_aritySuffixesOneToNine = { "`1", "`2", "`3", "`4", "`5", "`6", "`7", "`8", "`9" };

    internal static string GetAritySuffix(int arity)
    {
        Debug.Assert(arity > 0);
        return (arity <= 9) ? s_aritySuffixesOneToNine[arity - 1] : string.Concat(GenericTypeNameManglingString, arity.ToString(CultureInfo.InvariantCulture));
    }

#nullable enable
    internal static string ComposeAritySuffixedMetadataName(string name, int arity, string? associatedFileIdentifier)
    {
        return associatedFileIdentifier + (arity == 0 ? name : name + GetAritySuffix(arity));
    }
#nullable disable

    internal static int InferTypeArityFromMetadataName(string emittedTypeName)
    {
        int suffixStartsAt;
        return InferTypeArityFromMetadataName(emittedTypeName, out suffixStartsAt);
    }

    private static short InferTypeArityFromMetadataName(string emittedTypeName, out int suffixStartsAt)
    {
        Debug.Assert(emittedTypeName != null, "NULL actual name unexpected!!!");
        int emittedTypeNameLength = emittedTypeName.Length;

        int indexOfManglingChar;
        for (indexOfManglingChar = emittedTypeNameLength; indexOfManglingChar >= 1; indexOfManglingChar--)
        {
            if (emittedTypeName[indexOfManglingChar - 1] == GenericTypeNameManglingChar)
            {
                break;
            }
        }

        if (indexOfManglingChar < 2 ||
           (emittedTypeNameLength - indexOfManglingChar) == 0 ||
           emittedTypeNameLength - indexOfManglingChar > MaxStringLengthForParamSize)
        {
            suffixStartsAt = -1;
            return 0;
        }

        // Given a name corresponding to <unmangledName>`<arity>,
        // extract the arity.
        string stringRepresentingArity = emittedTypeName.Substring(indexOfManglingChar);

        int arity;
        bool nonNumericCharFound = !int.TryParse(stringRepresentingArity, NumberStyles.None, CultureInfo.InvariantCulture, out arity);

        if (nonNumericCharFound || arity < 0 || arity > short.MaxValue ||
            stringRepresentingArity != arity.ToString())
        {
            suffixStartsAt = -1;
            return 0;
        }

        suffixStartsAt = indexOfManglingChar - 1;
        return (short)arity;
    }

    internal static string InferTypeArityAndUnmangleMetadataName(string emittedTypeName, out short arity)
    {
        int suffixStartsAt;
        arity = InferTypeArityFromMetadataName(emittedTypeName, out suffixStartsAt);

        if (arity == 0)
        {
            Debug.Assert(suffixStartsAt == -1);
            return emittedTypeName;
        }

        Debug.Assert(suffixStartsAt > 0 && suffixStartsAt < emittedTypeName.Length - 1);
        return emittedTypeName.Substring(0, suffixStartsAt);
    }

    internal static string UnmangleMetadataNameForArity(string emittedTypeName, int arity)
    {
        Debug.Assert(arity > 0);

        int suffixStartsAt;
        if (arity == InferTypeArityFromMetadataName(emittedTypeName, out suffixStartsAt))
        {
            Debug.Assert(suffixStartsAt > 0 && suffixStartsAt < emittedTypeName.Length - 1);
            return emittedTypeName.Substring(0, suffixStartsAt);
        }

        return emittedTypeName;
    }

    /// <summary>
    /// An ImmutableArray representing the single string "System"
    /// </summary>
    private static readonly ImmutableArray<string> s_splitQualifiedNameSystem = ImmutableArray.Create(SystemString);

    internal static ImmutableArray<string> SplitQualifiedName(
          string name)
    {
        Debug.Assert(name != null);

        if (name.Length == 0)
        {
            return ImmutableArray<string>.Empty;
        }

        // PERF: Avoid String.Split because of the allocations. Also, we can special-case
        // for "System" if it is the first or only part.

        int dots = 0;
        foreach (char ch in name)
        {
            if (ch == DotDelimiter)
            {
                dots++;
            }
        }

        if (dots == 0)
        {
            return name == SystemString ? s_splitQualifiedNameSystem : ImmutableArray.Create(name);
        }

        var result = ArrayBuilder<string>.GetInstance(dots + 1);

        int start = 0;
        for (int i = 0; dots > 0; i++)
        {
            if (name[i] == DotDelimiter)
            {
                int len = i - start;
                if (len == 6 && start == 0 && name.StartsWith(SystemString, StringComparison.Ordinal))
                {
                    result.Add(SystemString);
                }
                else
                {
                    result.Add(name.Substring(start, len));
                }

                dots--;
                start = i + 1;
            }
        }

        result.Add(name.Substring(start));

        return result.ToImmutableAndFree();
    }

    internal static string SplitQualifiedName(
        string pstrName,
        out string qualifier)
    {
        Debug.Assert(pstrName != null);

        // In mangled names, the original unmangled name is frequently included,
        // surrounded by angle brackets.  The unmangled name may contain dots
        // (e.g. if it is an explicit interface implementation) or paired angle
        // brackets (e.g. if the explicitly implemented interface is generic).
        var angleBracketDepth = 0;
        var delimiter = -1;
        for (int i = 0; i < pstrName.Length; i++)
        {
            switch (pstrName[i])
            {
                case MangledNameRegionStartChar:
                    angleBracketDepth++;
                    break;
                case MangledNameRegionEndChar:
                    angleBracketDepth--;
                    break;
                case DotDelimiter:
                    // If we see consecutive dots, the second is part of the method name
                    // (i.e. ".ctor" or ".cctor").
                    if (angleBracketDepth == 0 && (i == 0 || delimiter < i - 1))
                    {
                        delimiter = i;
                    }
                    break;
            }
        }
        Debug.Assert(angleBracketDepth == 0);

        if (delimiter < 0)
        {
            qualifier = string.Empty;
            return pstrName;
        }

        if (delimiter == 6 && pstrName.StartsWith(SystemString, StringComparison.Ordinal))
        {
            qualifier = SystemString;
        }
        else
        {
            qualifier = pstrName.Substring(0, delimiter);
        }

        return pstrName.Substring(delimiter + 1);
    }

    internal static string BuildQualifiedName(
        string qualifier,
        string name)
    {
        Debug.Assert(name != null);

        if (!string.IsNullOrEmpty(qualifier))
        {
            return String.Concat(qualifier, DotDelimiterString, name);
        }

        return name;
    }

    /// <summary>
    /// Given an input string changes it to be acceptable as a part of a type name.
    /// </summary>
    internal static string MangleForTypeNameIfNeeded(string moduleName)
    {
        var pooledStrBuilder = PooledStringBuilder.GetInstance();
        var s = pooledStrBuilder.Builder;
        s.Append(moduleName);
        s.Replace("Q", "QQ");
        s.Replace("_", "Q_");
        s.Replace('.', '_');

        return pooledStrBuilder.ToStringAndFree();
    }
}