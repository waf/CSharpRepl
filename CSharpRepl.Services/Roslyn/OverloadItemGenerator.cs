// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using CSharpRepl.Services.Extensions;
using CSharpRepl.Services.SyntaxHighlighting;
using Microsoft.CodeAnalysis;
using PrettyPrompt.Completion;
using PrettyPrompt.Documents;
using PrettyPrompt.Highlighting;

namespace CSharpRepl.Services.Roslyn;

internal class OverloadItemGenerator
{
    private readonly FormattedStringBuilder signatureBuilder = new();
    private readonly SyntaxHighlighter highlighter;

    public OverloadItemGenerator(SyntaxHighlighter highlighter)
    {
        this.highlighter = highlighter;
    }

    public OverloadItem Create(ISymbol symbol, ImmutableArray<IParameterSymbol> symbolParameters, int argumentIndex, SemanticModel semanticModel, CancellationToken cancellationToken)
    {
        var signature = ToSignature(symbol, argumentIndex < symbolParameters.Length ? symbolParameters[argumentIndex] : null);
        var xml = symbol.GetDocumentationCommentXml(cancellationToken: cancellationToken);
        var comment = DocumentationComment.FromXmlFragment(xml, semanticModel, highlighter);
        var parameters = symbolParameters.Select(p => new OverloadItem.Parameter(p.Name, comment.GetParameterText(p.Name))).ToArray();
        return new OverloadItem(signature, comment.SummaryText, comment.ReturnsText, parameters);
    }

    public OverloadItem Create(ITypeSymbol[] typeSymbols, int argumentIndex, SemanticModel semanticModel, CancellationToken cancellationToken)
    {
        Debug.Assert(typeSymbols.Length > 0);
        var containingSymbol = typeSymbols[0].ContainingSymbol;
        Debug.Assert(typeSymbols.All(t => SymbolEqualityComparer.Default.Equals(t.ContainingSymbol, containingSymbol)));

        var signature = ToSignature(containingSymbol, argumentIndex < typeSymbols.Length ? typeSymbols[argumentIndex] : null);
        var xml = containingSymbol.GetDocumentationCommentXml(cancellationToken: cancellationToken);
        var comment = DocumentationComment.FromXmlFragment(xml, semanticModel, highlighter);
        var typeParams = typeSymbols.Select(t => new OverloadItem.Parameter(t.Name, comment.GetTypeParameterText(t.Name))).ToArray();
        return new OverloadItem(signature, comment.SummaryText, comment.ReturnsText, typeParams);
    }

    private FormattedString ToSignature(ISymbol symbol, ISymbol? currentArgument)
    {
        TextSpan currentArgumentSpan = default;
        if (currentArgument != null)
        {
            var signatureText = symbol.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);
            var currentArgumentText = currentArgument.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);
            var index = signatureText.IndexOf(currentArgumentText);
            currentArgumentSpan =
                index == -1 ?
                default :
                new TextSpan(index, currentArgumentText.Length);
        }

        foreach (var part in symbol.ToDisplayParts(SymbolDisplayFormat.MinimallyQualifiedFormat))
        {
            var partText = part.ToString();
            var classification = RoslynExtensions.SymbolDisplayPartKindToClassificationTypeName(part.Kind);
            if (highlighter.TryGetAnsiColor(classification, out var color))
            {
                bool bold = false;
                if (currentArgumentSpan.Length > 0 &&
                    signatureBuilder.Length >= currentArgumentSpan.Start &&
                    signatureBuilder.Length + partText.Length <= currentArgumentSpan.End)
                {
                    bold = true;
                }

                var format = new ConsoleFormat(color, Bold: bold);
                signatureBuilder.Append(partText, format);
            }
            else
            {
                signatureBuilder.Append(partText);
            }
        }
        var result = signatureBuilder.ToFormattedString();
        signatureBuilder.Clear();
        return result;
    }

    private record struct ParsedXmlDocumentation(
        FormattedString Summary,
        FormattedString ReturnDescription,
        IReadOnlyList<OverloadItem.Parameter> Parameters,
        IReadOnlyList<OverloadItem.Parameter> TypeParameters)
    {
        public OverloadItem.Parameter GetParameter(string name) => GetParameter(Parameters, name);
        public OverloadItem.Parameter GetTypeParameter(string name) => GetParameter(TypeParameters, name);

        private static OverloadItem.Parameter GetParameter(IReadOnlyList<OverloadItem.Parameter> values, string name)
        {
            foreach (var param in values)
            {
                if (param.Name == name) return param;
            }
            return new OverloadItem.Parameter(name, FormattedString.Empty);
        }
    }
}