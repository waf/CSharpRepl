// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Xml;
using CSharpRepl.Services.Extensions;
using CSharpRepl.Services.SyntaxHighlighting;
using Microsoft.CodeAnalysis;
using PrettyPrompt.Completion;
using PrettyPrompt.Highlighting;

namespace CSharpRepl.Services.Roslyn;

internal class OverloadItemGenerator
{
    private const char LineEnd = '\n';

    private readonly FormattedStringBuilder summaryBuilder = new();
    private readonly FormattedStringBuilder returnBuilder = new();
    private readonly FormattedStringBuilder parameterBuilder = new();
    private readonly FormattedStringBuilder typeParameterBuilder = new();
    private readonly FormattedStringBuilder signatureBuilder = new();
    private readonly SyntaxHighlighter highlighter;

    public OverloadItemGenerator(SyntaxHighlighter highlighter)
    {
        this.highlighter = highlighter;
    }

    public OverloadItem Create(ISymbol symbol, CancellationToken cancellationToken)
    {
        var signature = ToSignature(symbol);
        var xml = symbol.GetDocumentationCommentXml(cancellationToken: cancellationToken);

        //TODO https://github.com/waf/CSharpRepl/issues/164

        if (TryParse(xml, out var parsedXml))
        {
            return new OverloadItem(signature, parsedXml.Summary, parsedXml.ReturnDescription, parsedXml.Parameters);
        }
        else
        {
            return new OverloadItem(signature, FormattedString.Empty, FormattedString.Empty, Array.Empty<OverloadItem.Parameter>());
        }
    }

    public OverloadItem Create(ITypeSymbol[] typeSymbols, CancellationToken cancellationToken)
    {
        Debug.Assert(typeSymbols.Length > 0);
        var containingSymbol = typeSymbols[0].ContainingSymbol;
        Debug.Assert(typeSymbols.All(t => SymbolEqualityComparer.Default.Equals(t.ContainingSymbol, containingSymbol)));

        var signature = ToSignature(containingSymbol);
        var xml = containingSymbol.GetDocumentationCommentXml(cancellationToken: cancellationToken);
        if (TryParse(xml, out var parsedXml))
        {
            var typeParams = typeSymbols.Select(t => parsedXml.GetTypeParameter(t.Name)).ToArray();
            return new OverloadItem(signature, parsedXml.Summary, parsedXml.ReturnDescription, typeParams);
        }
        else
        {
            var typeParams = typeSymbols.Select(t => new OverloadItem.Parameter(t.Name, FormattedString.Empty)).ToArray();
            return new OverloadItem(signature, FormattedString.Empty, FormattedString.Empty, typeParams);
        }
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

    private bool TryParse(string? xmlDocumentation, out ParsedXmlDocumentation result)
    {
        if (string.IsNullOrEmpty(xmlDocumentation))
        {
            result = default;
            return false;
        }

        var parameters = new List<OverloadItem.Parameter>();
        var typeParameters = new List<OverloadItem.Parameter>();
        FormattedStringBuilder currentBuilder = default;
        string? currentParameter = null;
        string? currentTypeParameter = null;
        using var reader = new StringReader("<docroot>" + xmlDocumentation + "</docroot>");
        using var xml = XmlReader.Create(reader);
        while (xml.Read())
        {
            string? elementName = null;
            if (xml.NodeType == XmlNodeType.Element)
            {
                elementName = xml.Name.ToLowerInvariant();
                switch (elementName)
                {
                    case "docroot":
                        break;

                    //TODO
                    //case "filterpriority":
                    //case "remarks":
                    //case "example":
                    //case "value":
                    //case "exception":
                    //case "paramref":
                    //case "typeparamref":

                    case "see":
                    case "seealso":
                        var value = xml["cref"];
                        if (value != null)
                        {
                            //TODO type name simplicifiction/coloring/generic args/...
                            currentBuilder.Append(' ');
                            AppendMultiLineString(currentBuilder, value);
                            currentBuilder.Append(' ');
                        }
                        else
                        {
                            value = xml["href"] ?? xml["langword"];
                            if (value != null)
                            {
                                currentBuilder.Append(' ');
                                AppendMultiLineString(currentBuilder, value);
                                currentBuilder.Append(' ');
                            }
                        }
                        break;

                    case "returns":
                        ChangeCurrentBuilder(returnBuilder);
                        break;

                    case "summary":
                        ChangeCurrentBuilder(summaryBuilder);
                        break;

                    case "param":
                        ChangeCurrentBuilder(parameterBuilder);
                        currentParameter = xml["name"];
                        break;

                    case "typeparam":
                        ChangeCurrentBuilder(typeParameterBuilder);
                        currentTypeParameter = xml["name"];
                        break;

                    case "br":
                    case "para":
                        if (!currentBuilder.IsDefault)
                        {
                            currentBuilder.Append(LineEnd);
                        }
                        break;

                    default:
                        xml.Skip();
                        break;
                }
            }
            else if (xml.NodeType == XmlNodeType.Text && !currentBuilder.IsDefault)
            {
                if (elementName == "code")
                {
                    currentBuilder.Append(xml.Value);
                }
                else
                {
                    AppendMultiLineString(currentBuilder, xml.Value);
                }
            }
        }

        ChangeCurrentBuilder(default);
        result = new ParsedXmlDocumentation(summaryBuilder.ToFormattedString(), returnBuilder.ToFormattedString(), parameters, typeParameters);

        summaryBuilder.Clear();
        returnBuilder.Clear();

        return true;

        void ChangeCurrentBuilder(FormattedStringBuilder builder)
        {
            if (currentBuilder == parameterBuilder)
            {
                parameters.Add(new OverloadItem.Parameter(currentParameter ?? "", parameterBuilder.ToFormattedString()));
                parameterBuilder.Clear();
            }
            if (currentBuilder == typeParameterBuilder)
            {
                typeParameters.Add(new OverloadItem.Parameter(currentTypeParameter ?? "", typeParameterBuilder.ToFormattedString()));
                typeParameterBuilder.Clear();
            }
            currentBuilder = builder;
        }
    }

    private void AppendMultiLineString(FormattedStringBuilder builder, string? input)
    {
        if (input is null) return;

        var lines = input.Split(new string[] { "\n", "\r\n" }, StringSplitOptions.RemoveEmptyEntries);
        for (int i = 0; i < lines.Length; i++)
        {
            builder.Append(lines[i].AsSpan().Trim());
            if (i < lines.Length - 1) builder.Append(LineEnd);
        }
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