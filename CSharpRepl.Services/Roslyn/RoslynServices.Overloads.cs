// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ICSharpCode.Decompiler.CSharp.Syntax;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using PrettyPromptOverloadItem = PrettyPrompt.Completion.OverloadItem;

namespace CSharpRepl.Services.Roslyn;

/// <summary>
/// The main entry point of all services. This is a facade for other services that manages their startup and initialization.
/// It also ensures two different areas of the Roslyn API, the Scripting and Workspace APIs, remain in sync.
/// </summary>
public sealed partial class RoslynServices
{
    public async Task<(IReadOnlyList<PrettyPromptOverloadItem> Overloads, int ArgumentIndex)> GetOverloadsAsync(string text, int caret, CancellationToken cancellationToken)
    {
        if (caret > 0)
        {
            await Initialization.ConfigureAwait(false);

            var sourceText = SourceText.From(text);
            var document = workspaceManager.CurrentDocument.WithText(sourceText);

            var tree = await document.GetSyntaxTreeAsync(cancellationToken);
            if (tree is null) return Empty();

            var root = await tree.GetRootAsync(cancellationToken);

            var node = FindNonWhitespaceNode(text, root, caret);
            if (node is null) return Empty();

            var result = await GetGenericOverloadsWhenThereIsNoTypeArgListYetAsync(document, root);
            if (result.Overloads.Count > 0) return result;

            while (
                !node.IsKind(SyntaxKind.ArgumentList) &&
                !node.IsKind(SyntaxKind.BracketedArgumentList) &&
                !node.IsKind(SyntaxKind.TypeArgumentList))
            {
                node = node.Parent;
                if (node is null) return Empty();
            }

            return await GetOverloadsForArgList(document, node);
        }

        return Empty();

        static (IReadOnlyList<PrettyPromptOverloadItem> Overloads, int ArgumentIndex) Empty() => (Array.Empty<PrettyPromptOverloadItem>(), 0);

        async Task<(IReadOnlyList<PrettyPromptOverloadItem> Overloads, int ArgumentIndex)> GetOverloadsForArgList(Document document, SyntaxNode argList)
        {
            var argListSpan = argList.GetLocation().SourceSpan;
            if (caret <= argListSpan.Start)
            {
                //we are before opening parenthesis of arg list

                if (TryGetArgListParent(argList.Parent, out var parentArgList))
                {
                    //we could be nested in multiple arg lists
                    return await GetOverloadsForArgList(document, parentArgList);
                }

                return Empty();
            }

            var closeParenToken =
                (argList as ArgumentListSyntax)?.CloseParenToken ??
                (argList as BracketedArgumentListSyntax)?.CloseBracketToken ??
                (argList as TypeArgumentListSyntax)?.GreaterThanToken;
            if (closeParenToken?.Span.Length > 0 && caret >= argListSpan.End)
            {
                //we are after closing parenthesis of arg list

                if (TryGetArgListParent(argList.Parent, out var parentArgList))
                {
                    //we could be nested in multiple arg lists
                    return await GetOverloadsForArgList(document, parentArgList);
                }

                return Empty();
            }

            var semanticModel = await document.GetSemanticModelAsync(cancellationToken);
            if (semanticModel is null) return Empty();

            var symbols = GetMemberGroup(semanticModel, argList.Parent, cancellationToken);
            if (symbols.Count > 0)
            {
                var items = new List<PrettyPromptOverloadItem>(symbols.Count);
                foreach (var symbol in symbols)
                {
                    switch (symbol)
                    {
                        case IMethodSymbol method:
                            items.Add(overloadItemGenerator.Value!.Create(method, method.Parameters, cancellationToken));
                            break;
                        case IPropertySymbol property:
                            items.Add(overloadItemGenerator.Value!.Create(property, property.Parameters, cancellationToken));
                            break;
                        case ITypeSymbol[] or ITypeParameterSymbol[]:
                            items.Add(overloadItemGenerator.Value!.Create((ITypeSymbol[])symbol, cancellationToken));
                            break;
                        default:
                            Debug.Fail("unable to get oveload info");
                            break;
                    }
                }

                int argIndex = 0;
                var argSeparators =
                    (argList as BaseArgumentListSyntax)?.Arguments.GetSeparators() ??
                    (argList as TypeArgumentListSyntax)?.Arguments.GetSeparators();
                if (argSeparators is null) return Empty();
                foreach (var separator in argSeparators)
                {
                    if (caret <= separator.SpanStart)
                    {
                        break;
                    }
                    ++argIndex;
                }

                return (items, argIndex);
            }
            else
            {
                return Empty();
            }
        }

        async Task<(IReadOnlyList<PrettyPromptOverloadItem> Overloads, int ArgumentIndex)> GetGenericOverloadsWhenThereIsNoTypeArgListYetAsync(Document document, SyntaxNode root)
        {
            int lessThanTokenIndex = -1;
            int parity = 0;
            for (int i = caret - 1; i >= 0; i--)
            {
                var c = text[i];
                if (c == '<')
                {
                    if (parity == 0)
                    {
                        lessThanTokenIndex = i;
                        break;
                    }
                    parity++;
                }
                else if (c == '>')
                {
                    parity--;
                    break;
                }
            }
            if (lessThanTokenIndex == -1) return Empty();

            if (root.FindToken(lessThanTokenIndex).Parent is BinaryExpressionSyntax { RawKind: (int)SyntaxKind.LessThanExpression } binaryExpression)
            {
                if (binaryExpression.Left.DescendantNodesAndSelf().Last() is IdentifierNameSyntax identifierName)
                {
                    if (caret <= binaryExpression.OperatorToken.SpanStart) return Empty();

                    var semanticModel = await document.GetSemanticModelAsync(cancellationToken);
                    if (semanticModel is null) return Empty();

                    var symbols = LookupGenericMethodsAndTypes(semanticModel, identifierName.SpanStart, identifierName.Identifier.ValueText, cancellationToken: cancellationToken);
                    var items = new List<PrettyPromptOverloadItem>(symbols.Length);
                    foreach (var symbol in symbols)
                    {
                        switch (symbol)
                        {
                            case IMethodSymbol method:
                                items.Add(overloadItemGenerator.Value!.Create(method.TypeParameters.ToArray(), cancellationToken));
                                break;
                            case INamedTypeSymbol type:
                                items.Add(overloadItemGenerator.Value!.Create(type.TypeParameters.ToArray(), cancellationToken));
                                break;
                            default:
                                Debug.Fail("unable to get oveload info");
                                break;
                        }
                    }

                    int argumentIndex = 0;
                    for (int i = binaryExpression.OperatorToken.Span.End; i < caret; i++)
                    {
                        if (text[i] == ',') argumentIndex++;
                    }

                    return (items, argumentIndex);
                }
            }

            return Empty();
        }

        static bool TryGetArgListParent(SyntaxNode? node, [NotNullWhen(true)] out ArgumentListSyntax? result)
        {
            while (node != null)
            {
                if (node is ArgumentListSyntax argList)
                {
                    result = argList;
                    return true;
                }
                node = node.Parent;
            }
            result = null;
            return false;
        }

        static IReadOnlyList<object> GetMemberGroup(SemanticModel semanticModel, SyntaxNode? node, CancellationToken cancellationToken)
        {
            if (node is InvocationExpressionSyntax invocationExpression)
            {
                return semanticModel.GetMemberGroup(invocationExpression.Expression, cancellationToken);
            }
            else if (node is ObjectCreationExpressionSyntax objectCreationExpression)
            {
                return semanticModel.GetMemberGroup(objectCreationExpression, cancellationToken);
            }
            else if (node is ElementAccessExpressionSyntax elementAccessExpression)
            {
                return semanticModel.GetIndexerGroup(elementAccessExpression.Expression, cancellationToken).Cast<ISymbol>().ToImmutableArray();
            }
            else if (node is ConstructorInitializerSyntax constructorInitializer)
            {
                //TODO - this does not work because (i think this from debugging GetMemberGroup) it looks for oveloads of the 'caller ctor'
                //       we probably need to look for type and depending on if 'constructorInitializer' is 'base' or 'this' we need
                //       to manualy get overloads for base/this from semantic model
                //return semanticModel.GetMemberGroup(constructorInitializer, cancellationToken).Cast<ISymbol>().ToImmutableArray();
            }
            else if (node is GenericNameSyntax genericNameSyntax)
            {
                //This is more complex.
                //Suppose we have 'new MyClass<' or 'MyMethod<'. We don't know in advance which overload will be selected, so we
                //need to get all generic overloads, collect their type args, filter out duplicate sequences, and order by complexity.
                var group = GetMemberGroupGeneric(semanticModel, genericNameSyntax, cancellationToken);
                var typeArgs = new HashSet<ITypeSymbol[]>(TypeArgsSequenceComparer.Instance);
                foreach (var m in group)
                {
                    switch (m)
                    {
                        case IMethodSymbol method:
                            typeArgs.Add(method.TypeParameters.ToArray());
                            break;
                        case INamedTypeSymbol type:
                            typeArgs.Add(type.TypeParameters.ToArray());
                            break;
                        default:
                            Debug.Fail("unexpected case");
                            break;
                    }
                }
                return typeArgs.OrderBy(s => s.Length).ToArray();
            }
            else
            {
                Debug.Fail("unexpected case");
            }
            return ImmutableArray<object>.Empty;
        }

        static IReadOnlyList<object> GetMemberGroupGeneric(SemanticModel semanticModel, GenericNameSyntax genericNameSyntax, CancellationToken cancellationToken)
        {
            SyntaxNode? node = genericNameSyntax.Parent;
            while (node != null && node is QualifiedNameSyntax)
            {
                node = node.Parent;
            }

            if (node is MemberAccessExpressionSyntax memberAccessExpression)
            {
                if (memberAccessExpression.Name == genericNameSyntax)
                {
                    //e.g.: MyType.Method<string, int>()
                    return LookupGenericMethodsAndTypes(
                        semanticModel,
                        memberAccessExpression.Name.SpanStart,
                        name: genericNameSyntax.Identifier.ValueText,
                        containerTypeSyntax: memberAccessExpression.Expression,
                        cancellationToken);
                }

                //e.g.: MyType<string, int>.Equals()
                node = null;
            }
            if (node is ObjectCreationExpressionSyntax objectCreationExpression)
            {
                GenericNameSyntax? type = null;
                INamespaceSymbol? typeNamespace = null;
                if (objectCreationExpression.Type is GenericNameSyntax typeGenericNameSyntax)
                {
                    type = typeGenericNameSyntax;
                }
                else if (objectCreationExpression.Type is QualifiedNameSyntax typeQualifiedName)
                {
                    type = typeQualifiedName.Right as GenericNameSyntax;
                    typeNamespace = semanticModel.GetSymbolInfo(typeQualifiedName.Left, cancellationToken).Symbol as INamespaceSymbol;
                }

                if (type is null)
                {
                    Debug.Fail("unexpected case");
                    return ImmutableArray<object>.Empty;
                }

                return semanticModel.LookupNamespacesAndTypes(node.SpanStart, name: type.Identifier.ValueText)
                    .OfType<INamedTypeSymbol>()
                    .Where(t => t.IsGenericType && IsSubnamespace(t.ContainingNamespace, typeNamespace))
                    .ToArray();
            }
            if (node is null or InvocationExpressionSyntax or ExpressionStatementSyntax or TypeArgumentListSyntax)
            {
                return LookupGenericMethodsAndTypes(
                    semanticModel,
                    genericNameSyntax.SpanStart,
                    name: genericNameSyntax.Identifier.ValueText,
                    cancellationToken: cancellationToken);
            }

            Debug.Fail("unexpected case");
            return ImmutableArray<object>.Empty;
        }

        static bool IsSubnamespace(INamespaceSymbol? @namespace, INamespaceSymbol? subnamespace)
        {
            while (true)
            {
                if (subnamespace is null) return true;
                if (@namespace is null) return false;

                if (@namespace.Name != subnamespace.Name) return false;
                @namespace = @namespace.ContainingNamespace;
                subnamespace = subnamespace.ContainingNamespace;
            }
        }
    }

    private static ISymbol[] LookupGenericMethodsAndTypes(SemanticModel semanticModel, int location, string name, SyntaxNode? containerTypeSyntax = null, CancellationToken cancellationToken = default)
    {
        ITypeSymbol? containerType = null;
        if (containerTypeSyntax != null)
        {
            containerType = semanticModel.GetTypeInfo(containerTypeSyntax, cancellationToken).Type;
        }

        return semanticModel.LookupSymbols(location, containerType, name)
            .Where(s =>
                (s is IMethodSymbol m && m.IsGenericMethod) ||
                (s is INamedTypeSymbol t && t.IsGenericType))
            .ToArray();
    }

    private sealed class TypeArgsSequenceComparer : IEqualityComparer<ITypeSymbol[]>, IComparer<ITypeSymbol[]>
    {
        public static readonly TypeArgsSequenceComparer Instance = new();

        private TypeArgsSequenceComparer() { }

        public int Compare(ITypeSymbol[]? x, ITypeSymbol[]? y)
        {
            if (x is null)
            {
                return y is null ? 0 : -1;
            }
            else
            {
                if (y is null) return 1;
                return x.Length.CompareTo(y.Length);
            }
        }

        public bool Equals(ITypeSymbol[]? x, ITypeSymbol[]? y)
        {
            if (x is null)
            {
                return y is null;
            }
            else
            {
                if (y is null || x.Length != y.Length) return false;

                for (int i = 0; i < x.Length; i++)
                {
                    if (!SymbolEqualityComparer.Default.Equals(x[i], y[i])) return false;
                }
                return true;
            }
        }

        public int GetHashCode([DisallowNull] ITypeSymbol[] obj)
        {
            var hash = new HashCode();
            foreach (var t in obj)
            {
                hash.Add(t, SymbolEqualityComparer.Default);
            }
            return hash.ToHashCode();
        }
    }
}