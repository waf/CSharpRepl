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

    public OverloadItem Create(ISymbol symbol, ImmutableArray<IParameterSymbol> symbolParameters, CancellationToken cancellationToken)
    {
        var signature = ToSignature(symbol);
        var xml = symbol.GetDocumentationCommentXml(cancellationToken: cancellationToken);
        var comment = DocumentationComment.FromXmlFragment(xml);
        var parameters = symbolParameters.Select(p => new OverloadItem.Parameter(p.Name, comment.GetParameterText(p.Name))).ToArray();
        return new OverloadItem(signature, comment.SummaryText, comment.ReturnsText, parameters);
    }

    public OverloadItem Create(ITypeSymbol[] typeSymbols, CancellationToken cancellationToken)
    {
        Debug.Assert(typeSymbols.Length > 0);
        var containingSymbol = typeSymbols[0].ContainingSymbol;
        Debug.Assert(typeSymbols.All(t => SymbolEqualityComparer.Default.Equals(t.ContainingSymbol, containingSymbol)));

        var signature = ToSignature(containingSymbol);
        var xml = containingSymbol.GetDocumentationCommentXml(cancellationToken: cancellationToken);
        var comment = DocumentationComment.FromXmlFragment(xml);
        var typeParams = comment.ParameterNames.Select(n => new OverloadItem.Parameter(n, comment.GetTypeParameterText(n))).ToArray();
        return new OverloadItem(signature, comment.SummaryText, comment.ReturnsText, typeParams);
    }

    private FormattedString ToSignature(ISymbol containingSymbol)
    {
        foreach (var part in containingSymbol.ToDisplayParts(SymbolDisplayFormat.MinimallyQualifiedFormat))
        {
            var classification = RoslynExtensions.SymbolDisplayPartKindToClassificationTypeName(part.Kind);
            if (classification is not null &&
                highlighter.TryGetColor(classification, out var color))
            {
                signatureBuilder.Append(part.ToString(), new ConsoleFormat(color));
            }
            else
            {
                signatureBuilder.Append(part.ToString());
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