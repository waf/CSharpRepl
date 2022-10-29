// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Classification;

namespace CSharpRepl.Services.Extensions;

internal static class RoslynExtensions
{
    public static Solution ApplyChanges(this Solution edit, Workspace workspace)
    {
        if (!workspace.TryApplyChanges(edit))
        {
            throw new InvalidOperationException("Failed to apply edit to workspace");
        }
        return workspace.CurrentSolution;
    }

    //Source: https://github.com/dotnet/roslyn/blob/main/src/Features/Core/Portable/Common/TaggedText.cs
    //Unfortunately this method is internal, so here is copy.
    public static string? TextTagToClassificationTypeName(string taggedTextTag)
    {
        return taggedTextTag switch
        {
            TextTags.Keyword => ClassificationTypeNames.Keyword,
            TextTags.Class => ClassificationTypeNames.ClassName,
            TextTags.Delegate => ClassificationTypeNames.DelegateName,
            TextTags.Enum => ClassificationTypeNames.EnumName,
            TextTags.Interface => ClassificationTypeNames.InterfaceName,
            TextTags.Module => ClassificationTypeNames.ModuleName,
            TextTags.Struct or "Structure" => ClassificationTypeNames.StructName,
            TextTags.TypeParameter => ClassificationTypeNames.TypeParameterName,
            TextTags.Field => ClassificationTypeNames.FieldName,
            TextTags.Event => ClassificationTypeNames.EventName,
            TextTags.Label => ClassificationTypeNames.LabelName,
            TextTags.Local => ClassificationTypeNames.LocalName,
            TextTags.Method => ClassificationTypeNames.MethodName,
            TextTags.Namespace => ClassificationTypeNames.NamespaceName,
            TextTags.Parameter => ClassificationTypeNames.ParameterName,
            TextTags.Property => ClassificationTypeNames.PropertyName,
            TextTags.ExtensionMethod => ClassificationTypeNames.ExtensionMethodName,
            TextTags.EnumMember => ClassificationTypeNames.EnumMemberName,
            TextTags.Constant => ClassificationTypeNames.ConstantName,
            TextTags.Alias or TextTags.Assembly or TextTags.ErrorType or TextTags.RangeVariable => ClassificationTypeNames.Identifier,
            TextTags.NumericLiteral => ClassificationTypeNames.NumericLiteral,
            TextTags.StringLiteral => ClassificationTypeNames.StringLiteral,
            TextTags.Space or TextTags.LineBreak => ClassificationTypeNames.WhiteSpace,
            TextTags.Operator => ClassificationTypeNames.Operator,
            TextTags.Punctuation => ClassificationTypeNames.Punctuation,
            TextTags.AnonymousTypeIndicator or TextTags.Text => ClassificationTypeNames.Text,
            TextTags.Record => ClassificationTypeNames.RecordClassName,
            TextTags.RecordStruct => ClassificationTypeNames.RecordStructName,
            _ => null,
        };
    }

    //TextTagToClassificationTypeName analogue
    public static string? SymbolDisplayPartKindToClassificationTypeName(SymbolDisplayPartKind kind)
    {
        return kind switch
        {
            SymbolDisplayPartKind.Keyword => ClassificationTypeNames.Keyword,
            SymbolDisplayPartKind.ClassName => ClassificationTypeNames.ClassName,
            SymbolDisplayPartKind.DelegateName => ClassificationTypeNames.DelegateName,
            SymbolDisplayPartKind.EnumName => ClassificationTypeNames.EnumName,
            SymbolDisplayPartKind.InterfaceName => ClassificationTypeNames.InterfaceName,
            SymbolDisplayPartKind.ModuleName => ClassificationTypeNames.ModuleName,
            SymbolDisplayPartKind.StructName => ClassificationTypeNames.StructName,
            SymbolDisplayPartKind.TypeParameterName => ClassificationTypeNames.TypeParameterName,
            SymbolDisplayPartKind.FieldName => ClassificationTypeNames.FieldName,
            SymbolDisplayPartKind.EventName => ClassificationTypeNames.EventName,
            SymbolDisplayPartKind.LabelName => ClassificationTypeNames.LabelName,
            SymbolDisplayPartKind.LocalName => ClassificationTypeNames.LocalName,
            SymbolDisplayPartKind.MethodName => ClassificationTypeNames.MethodName,
            SymbolDisplayPartKind.NamespaceName => ClassificationTypeNames.NamespaceName,
            SymbolDisplayPartKind.ParameterName => ClassificationTypeNames.ParameterName,
            SymbolDisplayPartKind.PropertyName => ClassificationTypeNames.PropertyName,
            SymbolDisplayPartKind.ExtensionMethodName => ClassificationTypeNames.ExtensionMethodName,
            SymbolDisplayPartKind.EnumMemberName => ClassificationTypeNames.EnumMemberName,
            SymbolDisplayPartKind.ConstantName => ClassificationTypeNames.ConstantName,
            SymbolDisplayPartKind.AliasName or SymbolDisplayPartKind.AssemblyName or SymbolDisplayPartKind.ErrorTypeName or SymbolDisplayPartKind.RangeVariableName => ClassificationTypeNames.Identifier,
            SymbolDisplayPartKind.NumericLiteral => ClassificationTypeNames.NumericLiteral,
            SymbolDisplayPartKind.StringLiteral => ClassificationTypeNames.StringLiteral,
            SymbolDisplayPartKind.Space or SymbolDisplayPartKind.LineBreak => ClassificationTypeNames.WhiteSpace,
            SymbolDisplayPartKind.Operator => ClassificationTypeNames.Operator,
            SymbolDisplayPartKind.Punctuation => ClassificationTypeNames.Punctuation,
            SymbolDisplayPartKind.AnonymousTypeIndicator or SymbolDisplayPartKind.Text => ClassificationTypeNames.Text,
            SymbolDisplayPartKind.RecordClassName => ClassificationTypeNames.RecordClassName,
            SymbolDisplayPartKind.RecordStructName => ClassificationTypeNames.RecordStructName,
            _ => null,
        };
    }

    public static string? MemberTypeToClassificationTypeName(MemberTypes type)
    {
        return type switch
        {
            MemberTypes.Constructor or MemberTypes.Method => ClassificationTypeNames.MethodName,
            MemberTypes.Event => ClassificationTypeNames.EventName,
            MemberTypes.Field => ClassificationTypeNames.FieldName,
            MemberTypes.Property => ClassificationTypeNames.PropertyName,
            _ => null
        };
    }
}