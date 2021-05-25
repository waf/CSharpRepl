using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using System.Linq;
using System.Threading.Tasks;

namespace Sharply.Services.SymbolExploration
{
    class SymbolExplorer
    {
        private readonly SymbolDisplayFormat displayOptions;

        public SymbolExplorer()
        {
             this.displayOptions = new SymbolDisplayFormat(
                 SymbolDisplayGlobalNamespaceStyle.Omitted,
                 SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces,
                 SymbolDisplayGenericsOptions.None,
                 SymbolDisplayMemberOptions.IncludeContainingType,
                 SymbolDisplayDelegateStyle.NameOnly,
                 SymbolDisplayExtensionMethodStyle.StaticMethod,
                 SymbolDisplayParameterOptions.None,
                 SymbolDisplayPropertyStyle.NameOnly,
                 SymbolDisplayLocalOptions.None,
                 SymbolDisplayKindOptions.None,
                 SymbolDisplayMiscellaneousOptions.ExpandNullable
            );
        }

        public async Task<SymbolResult> GetSymbolAtPosition(Document document, int position)
        {
            var semanticModel = await document.GetSemanticModelAsync();
            var tree = semanticModel.SyntaxTree;

            // the most obvious way to implement this would be using GetEnclosingSymbol or ChildThatContainsPosition.
            // however, neither of those appears to work for script-type projects. GetEnclosingSymbol always returns "<Initialize>".
            var symbols =
                from node in tree.GetRoot().DescendantNodes()
                where node.Span.Start < position && position < node.Span.End
                orderby node.Span.Length
                let symbolInfo = semanticModel.GetSymbolInfo(node)
                select new { node, symbol = symbolInfo.Symbol ?? symbolInfo.CandidateSymbols.FirstOrDefault() };

            var mostSpecificSymbol = symbols.FirstOrDefault();

            if (mostSpecificSymbol is null) return SymbolResult.Unknown;

            return new SymbolResult(
                Kind: mostSpecificSymbol.node.DescendantNodesAndTokensAndSelf().Select(n => n.Kind().ToString()).ToArray(),
                SymbolDisplay: mostSpecificSymbol.symbol?.ToDisplayString(displayOptions)
            );
        }
    }

    public record SymbolResult(string[] Kind, string SymbolDisplay)
    {
        public static readonly SymbolResult Unknown = new SymbolResult(new[] { "Unknown" }, "Unknown");
    }
}
