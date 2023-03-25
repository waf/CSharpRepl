// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

//Modified version of
//  Microsoft.CodeAnalysis.CSharp.Scripting.Hosting.CSharpTypeNameFormatter and
//  Microsoft.CodeAnalysis.Scripting.Hosting.CommonTypeNameFormatter

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.Internal;
using System.Reflection;
using System.Text.RegularExpressions;
using CSharpRepl.Services.SyntaxHighlighting;
using CSharpRepl.Services.Theming;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Classification;
using Microsoft.CodeAnalysis.CSharp.Symbols;
using Microsoft.CodeAnalysis.PooledObjects;
using Microsoft.CodeAnalysis.Scripting.Hosting;
using Spectre.Console;

namespace CSharpRepl.Services.Roslyn.Formatting;

using static ObjectFormatterHelpers;
using TypeInfo = System.Reflection.TypeInfo;

internal sealed partial class TypeNameFormatter
{
    private const string GenericParameterOpening = "<";
    private const string GenericParameterClosing = ">";
    private const string ArrayOpening = "[";
    private const string ArrayClosing = "]";

    private readonly SyntaxHighlighter highlighter;

    public TypeNameFormatter(SyntaxHighlighter highlighter)
    {
        this.highlighter = highlighter;
    }

    public static Style GetTypeStyle(Type type, SyntaxHighlighter highlighter)
    {
        Style format;
        if (type.IsValueType) format = highlighter.GetStyle(ClassificationTypeNames.StructName);
        else if (type.IsInterface) format = highlighter.GetStyle(ClassificationTypeNames.InterfaceName);
        else if (type.IsSubclassOf(typeof(Delegate))) format = highlighter.GetStyle(ClassificationTypeNames.DelegateName);
        else format = highlighter.GetStyle(ClassificationTypeNames.ClassName);
        return format;
    }

    // TODO (tomat): Use DebuggerDisplay.Type if specified?
    public StyledString FormatTypeName(Type type, TypeNameFormatterOptions options, IList<string>? tupleNames = null)
    {
        if (type == null)
        {
            throw new ArgumentNullException(nameof(type));
        }

        if (GeneratedNameParser.TryParseSourceMethodNameFromGeneratedName(type.Name, GeneratedNameKind.StateMachineType, out var stateMachineName))
        {
            return stateMachineName;
        }

        var primitiveTypeName =
            options.UseLanguageKeywords ?
            GetPrimitiveTypeName(GetPrimitiveSpecialType(type)) :
            null;
        if (primitiveTypeName.HasValue)
        {
            return primitiveTypeName.Value;
        }

        if (type.IsGenericParameter || type.IsByRef && type.GetElementType()?.IsGenericParameter == true)
        {
            return new StyledString(type.Name, highlighter.GetStyle(ClassificationTypeNames.TypeParameterName));
        }

        if (type.IsArray)
        {
            return FormatArrayTypeName(type, options);
        }

        var typeInfo = type.GetTypeInfo();
        if (typeInfo.IsGenericType)
        {
            return FormatGenericTypeName(typeInfo, options, tupleNames);
        }

        return FormatNonGenericTypeName(typeInfo, options);
    }

    private StyledString FormatNonGenericTypeName(TypeInfo typeInfo, TypeNameFormatterOptions options)
    {
        var sb = new StyledStringBuilder();
        if (typeInfo.DeclaringType is null)
        {
            string[]? namespaceParts = null;
            if (options.ShowNamespaces && typeInfo.Namespace != null)
            {
                if (!(options.HideSystemNamespace && typeInfo.Namespace == nameof(System)))
                {
                    namespaceParts = typeInfo.Namespace.Split('.', StringSplitOptions.RemoveEmptyEntries);
                }
            }
            namespaceParts ??= Array.Empty<string>();

            var namespaceStyle = highlighter.GetStyle(ClassificationTypeNames.NamespaceName);
            for (int i = 0; i < namespaceParts.Length; i++)
            {
                sb.Append(namespaceParts[i], namespaceStyle);
                sb.Append('.');
            }
            AppendNameWithoutGenericPart(typeInfo, sb);
        }
        else
        {
            sb.Append(FormatTypeName(typeInfo.DeclaringType, options));
            sb.Append('.');
            AppendNameWithoutGenericPart(typeInfo, sb);
        }
        return sb.ToStyledString();

        void AppendNameWithoutGenericPart(TypeInfo typeInfo, StyledStringBuilder builder)
        {
            var typeStyle = GetTypeStyle(typeInfo, highlighter);
            var name = typeInfo.Name;

            if (SubmissionRegex().IsMatch(name)) //https://github.com/waf/CSharpRepl/issues/194)
            {
                return;
            }

            int backtick = name.IndexOf('`');
            if (backtick > 0)
            {
                builder.Append(name[..backtick], typeStyle);
            }
            else
            {
                builder.Append(name, typeStyle);
            }
        }
    }

    /// <summary>
    /// Formats an array type name (vector or multidimensional).
    /// </summary>
    public StyledString FormatArrayTypeName(Type arrayType, TypeNameFormatterOptions options)
    {
        if (arrayType == null)
        {
            throw new ArgumentNullException(nameof(arrayType));
        }

        var sb = new StyledStringBuilder();

        // print the inner-most element type first:
        var elementType = arrayType.GetElementType()!;
        while (elementType.IsArray)
        {
            elementType = elementType.GetElementType()!;
        }

        sb.Append(FormatTypeName(elementType, options));

        // print all components of a jagged array:
        var type = arrayType;
        do
        {
            AppendArrayRank(type);

            type = type.GetElementType()!;
        }
        while (type.IsArray);

        return sb.ToStyledString();

        void AppendArrayRank(Type arrayType)
        {
            sb.Append(ArrayOpening);
            int rank = arrayType.GetArrayRank();
            for (int i = 0; i < rank - 1; i++)
            {
                sb.Append(',');
            }
            sb.Append(ArrayClosing);
        }
    }

    private StyledString FormatGenericTypeName([MaybeNull] TypeInfo typeInfo, TypeNameFormatterOptions options, IList<string>? tupleNames)
    {
        var builder = new StyledStringBuilder();

        // consolidated generic arguments (includes arguments of all declaring types):
        // TODO (DevDiv #173210): shouldn't need parameters, but StackTrace gives us unconstructed symbols.
        var genericArguments = typeInfo.IsGenericTypeDefinition ? typeInfo.GenericTypeParameters : typeInfo.GenericTypeArguments;

        if (typeInfo.DeclaringType != null)
        {
            var nestedTypes = ArrayBuilder<TypeInfo>.GetInstance();
            do
            {
                nestedTypes.Add(typeInfo);
                typeInfo = typeInfo.DeclaringType?.GetTypeInfo();
            }
            while (typeInfo != null);

            int typeArgumentIndex = 0;
            for (int i = nestedTypes.Count - 1; i >= 0; i--)
            {
                AppendTypeInstantiation(nestedTypes[i], ref typeArgumentIndex);
                if (i > 0)
                {
                    builder.Append('.');
                }
            }

            nestedTypes.Free();
        }
        else
        {
            if (options.UseLanguageKeywords)
            {
                var nullableUnderlyingType = Nullable.GetUnderlyingType(typeInfo);
                if (nullableUnderlyingType != null)
                {
                    builder.Append(FormatTypeName(nullableUnderlyingType, options, tupleNames));
                    builder.Append('?');
                    return builder.ToStyledString();
                }
                if (typeInfo.IsValueTuple())
                {
                    builder.Append('(');
                    int currentArgCount = typeInfo.IsGenericTypeDefinition ? typeInfo.GenericTypeParameters.Length : typeInfo.GenericTypeArguments.Length;
                    for (int i = 0; i < currentArgCount; i++)
                    {
                        if (i > 0)
                        {
                            builder.Append(", ");
                        }
                        builder.Append(FormatTypeName(genericArguments[i], options));

                        Debug.Assert(tupleNames is null || tupleNames.Count == currentArgCount);
                        if (tupleNames != null && tupleNames.Count == currentArgCount)
                        {
                            builder.Append(' ');
                            builder.Append(tupleNames[i]);
                        }
                    }
                    builder.Append(')');
                    return builder.ToStyledString();
                }
            }

            int typeArgumentIndex = 0;
            AppendTypeInstantiation(typeInfo, ref typeArgumentIndex);
        }

        return builder.ToStyledString();

        void AppendTypeInstantiation(
            TypeInfo typeInfo,
            ref int genericArgIndex)
        {
            // generic arguments of all the outer types and the current type;
            int currentArgCount = (typeInfo.IsGenericTypeDefinition ? typeInfo.GenericTypeParameters.Length : typeInfo.GenericTypeArguments.Length) - genericArgIndex;

            var typeStyle = GetTypeStyle(typeInfo, highlighter);
            if (currentArgCount > 0)
            {
                builder.Append(FormatNonGenericTypeName(typeInfo, options));

                builder.Append(GenericParameterOpening);

                for (int i = 0; i < currentArgCount; i++)
                {
                    if (i > 0)
                    {
                        builder.Append(", ");
                    }
                    builder.Append(FormatTypeName(genericArguments[genericArgIndex++], options));
                }

                builder.Append(GenericParameterClosing);
            }
            else
            {
                builder.Append(typeInfo.Name, typeStyle);
            }
        }
    }

    private StyledString? GetPrimitiveTypeName(SpecialType type)
    {
        var resultText = Get(type);
        return
            resultText is null ?
            default(StyledString?) :
            new StyledString(resultText, highlighter.KeywordStyle);

        static string? Get(SpecialType type) => type switch
        {
            SpecialType.System_Boolean => "bool",
            SpecialType.System_Byte => "byte",
            SpecialType.System_Char => "char",
            SpecialType.System_Decimal => "decimal",
            SpecialType.System_Double => "double",
            SpecialType.System_Int16 => "short",
            SpecialType.System_Int32 => "int",
            SpecialType.System_Int64 => "long",
            SpecialType.System_SByte => "sbyte",
            SpecialType.System_Single => "float",
            SpecialType.System_String => "string",
            SpecialType.System_UInt16 => "ushort",
            SpecialType.System_UInt32 => "uint",
            SpecialType.System_UInt64 => "ulong",
            SpecialType.System_Object => "object",
            _ => null,
        };
    }

    [GeneratedRegex("Submission#[0-9]+")]
    private static partial Regex SubmissionRegex();
}