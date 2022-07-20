using System;
using System.Collections.Generic;
using System.IO;
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
    private readonly FormattedStringBuilder signatureBuilder = new();
    private readonly SyntaxHighlighter highlighter;

    public OverloadItemGenerator(SyntaxHighlighter highlighter)
    {
        this.highlighter = highlighter;
    }

    public OverloadItem? Create(ISymbol symbol, CancellationToken cancellationToken)
    {
        var xml = symbol.GetDocumentationCommentXml(cancellationToken: cancellationToken);
        if (TryParse(xml, out var parsedXml))
        {
            foreach (var part in symbol.ToDisplayParts(SymbolDisplayFormat.MinimallyQualifiedFormat))
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

            var result = new OverloadItem(signatureBuilder.ToFormattedString(), parsedXml.Summary, parsedXml.ReturnDescription, parsedXml.Parameters);
            signatureBuilder.Clear();
            return result;
        }
        return null;
    }

    private bool TryParse(string? xmlDocumentation, out (FormattedString Summary, FormattedString ReturnDescription, IReadOnlyList<OverloadItem.Parameter> Parameters) result)
    {
        if (string.IsNullOrEmpty(xmlDocumentation))
        {
            result = default;
            return false;
        }

        var parameters = new List<OverloadItem.Parameter>();
        FormattedStringBuilder currentBuilder = default;
        string? currentParameter = null;
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
                    //case "typeparam":
                    //case "value":
                    //case "exception":
                    //    xml.Skip();
                    //    break;

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

                    //TODO
                    //case "paramref":
                    //    currentSectionBuilder.Append(xml["name"]);
                    //    currentSectionBuilder.Append(' ');
                    //    break;

                    case "param":
                        ChangeCurrentBuilder(parameterBuilder);
                        currentParameter = xml["name"];
                        break;

                    //TODO
                    //case "typeparamref":
                    //    currentSectionBuilder.Append(xml["name"]);
                    //    currentSectionBuilder.Append(' ');
                    //    break;

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
        result = (summaryBuilder.ToFormattedString(), returnBuilder.ToFormattedString(), parameters);

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
}