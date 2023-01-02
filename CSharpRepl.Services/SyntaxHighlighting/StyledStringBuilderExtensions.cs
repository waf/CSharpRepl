using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Internal;
using System.Text.RegularExpressions;
using CSharpRepl.Services.Theming;
using Microsoft.CodeAnalysis.Classification;
using Spectre.Console;

namespace CSharpRepl.Services.SyntaxHighlighting;

internal static class StyledStringBuilderExtensions
{
    public static StyledStringBuilder AppendType(
        this StyledStringBuilder builder,
        Type? type,
        SyntaxHighlighter highlighter,
        bool fullName = true,
        IList<string>? tupleNames = null)
    {
        if (type is null) return builder;

        if (TypeNameHelper.BuiltInTypeNames.TryGetValue(type, out var typeName))
        {
            builder.Append(typeName, highlighter.KeywordStyle);
            return builder;
        }

        TypeNameHelperInternal.AppendTypeDisplayName(builder, type, fullName, includeGenericParameterNames: true, tupleNames, highlighter);

        return builder;
    }

    public static Style GetTypeStyle(Type type, SyntaxHighlighter highlighter)
    {
        Style format;
        if (type.IsValueType) format = new Style(foreground: highlighter.GetSpectreColor(ClassificationTypeNames.StructName));
        else if (type.IsInterface) format = new Style(foreground: highlighter.GetSpectreColor(ClassificationTypeNames.InterfaceName));
        else if (type.IsSubclassOf(typeof(Delegate))) format = new Style(foreground: highlighter.GetSpectreColor(ClassificationTypeNames.DelegateName));
        else format = new Style(foreground: highlighter.GetSpectreColor(ClassificationTypeNames.ClassName));
        return format;
    }

    //Modified version of https://github.com/benaadams/Ben.Demystifier/blob/main/src/Ben.Demystifier/TypeNameHelper.cs
    private static class TypeNameHelperInternal
    {
        public static StyledStringBuilder AppendTypeDisplayName(
            StyledStringBuilder builder,
            Type type,
            bool fullName,
            bool includeGenericParameterNames,
            IList<string>? tupleNames,
            SyntaxHighlighter highlighter)
        {
            ProcessType(builder, type, new DisplayNameOptions(fullName, includeGenericParameterNames), tupleNames, highlighter);
            return builder;
        }

        private static void ProcessType(
            StyledStringBuilder builder,
            Type type,
            DisplayNameOptions options,
            IList<string>? tupleNames,
            SyntaxHighlighter highlighter)
        {
            if (type.IsValueTuple())
            {
                builder.Append('(');
                var itemTypes = type.GenericTypeArguments;
                for (int i = 0; i < itemTypes.Length; i++)
                {
                    ProcessType(builder, itemTypes[i], new(fullName: false, includeGenericParameterNames: true), tupleNames: null, highlighter);
                    if (tupleNames != null && itemTypes.Length == tupleNames.Count)
                    {
                        builder.Append(' ');
                        builder.Append(tupleNames[i]);
                    }
                    if (i + 1 == itemTypes.Length)
                    {
                        continue;
                    }
                    builder.Append(", ");
                }
                builder.Append(')');
            }
            else if (type.IsGenericType)
            {
                var underlyingType = Nullable.GetUnderlyingType(type);
                if (underlyingType != null)
                {
                    ProcessType(builder, underlyingType, options, tupleNames, highlighter);
                    builder.Append('?');
                }
                else
                {
                    var genericArguments = type.GetGenericArguments();
                    ProcessGenericType(builder, type, genericArguments, genericArguments.Length, options, highlighter);
                }
            }
            else if (type.IsArray)
            {
                ProcessArrayType(builder, type, options, tupleNames, highlighter);
            }
            else if (TypeNameHelper.BuiltInTypeNames.TryGetValue(type, out var builtInName))
            {
                builder.Append(builtInName, highlighter.KeywordStyle);
            }
            else
            {
                var format = GetTypeStyle(type, highlighter);
                if (type.Namespace == nameof(System))
                {
                    builder.Append(type.Name, format);
                }
                else if (
                    type.Assembly.ManifestModule.Name == "FSharp.Core.dll" &&
                    TypeNameHelper.FSharpTypeNames.TryGetValue(type.Name, out builtInName))
                {
                    builder.Append(builtInName, format);
                }
                else if (type.IsGenericParameter)
                {
                    if (options.IncludeGenericParameterNames)
                    {
                        builder.Append(type.Name, new Style(foreground: highlighter.GetSpectreColor(ClassificationTypeNames.TypeParameterName)));
                    }
                }
                else
                {
                    var name = options.FullName ? type.FullName ?? type.Name : type.Name;
                    if (!Regex.IsMatch(name, @"Submission#[0-9]+")) //https://github.com/waf/CSharpRepl/issues/194)
                    {
                        builder.Append(name, format);
                    }
                }
            }
        }

        private static void ProcessArrayType(
            StyledStringBuilder builder,
            Type type,
            DisplayNameOptions options,
            IList<string>? tupleNames,
            SyntaxHighlighter highlighter)
        {
            var innerType = type;
            while (innerType.IsArray)
            {
                if (innerType.GetElementType() is { } inner)
                {
                    innerType = inner;
                }
            }

            ProcessType(builder, innerType, options, tupleNames, highlighter);

            while (type.IsArray)
            {
                builder.Append('[');
                var commaCount = type.GetArrayRank() - 1;
                for (int i = 0; i < commaCount; i++)
                {
                    builder.Append(',');
                }
                builder.Append(']');
                if (type.GetElementType() is not { } elementType)
                {
                    break;
                }
                type = elementType;
            }
        }

        private static void ProcessGenericType(StyledStringBuilder builder, Type type, Type[] genericArguments, int length, DisplayNameOptions options, SyntaxHighlighter highlighter)
        {
            var offset = 0;
            if (type.IsNested && type.DeclaringType is not null)
            {
                offset = type.DeclaringType.GetGenericArguments().Length;
            }

            if (options.FullName)
            {
                if (type.IsNested && type.DeclaringType is not null)
                {
                    ProcessGenericType(builder, type.DeclaringType, genericArguments, offset, options, highlighter);
                    builder.Append('+');
                }
                else if (!string.IsNullOrEmpty(type.Namespace))
                {
                    builder.Append(type.Namespace);
                    builder.Append('.');
                }
            }

            var format = GetTypeStyle(type, highlighter);

            var genericPartIndex = type.Name.IndexOf('`');
            if (genericPartIndex <= 0)
            {
                builder.Append(type.Name, format);
                return;
            }

            if (type.Assembly.ManifestModule.Name == "FSharp.Core.dll" &&
                TypeNameHelper.FSharpTypeNames.TryGetValue(type.Name, out var builtInName))
            {
                builder.Append(builtInName, format);
            }
            else
            {
                builder.Append(type.Name.AsSpan(0, genericPartIndex).ToString(), format);
            }

            builder.Append('<');
            for (var i = offset; i < length; i++)
            {
                ProcessType(builder, genericArguments[i], options, tupleNames: null, highlighter);
                if (i + 1 == length)
                {
                    continue;
                }

                builder.Append(',');
                if (options.IncludeGenericParameterNames || !genericArguments[i + 1].IsGenericParameter)
                {
                    builder.Append(' ');
                }
            }
            builder.Append('>');
        }

        private readonly struct DisplayNameOptions
        {
            public readonly bool FullName;
            public readonly bool IncludeGenericParameterNames;

            public DisplayNameOptions(bool fullName, bool includeGenericParameterNames)
            {
                FullName = fullName;
                IncludeGenericParameterNames = includeGenericParameterNames;
            }
        }
    }
}