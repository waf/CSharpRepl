using Microsoft.CodeAnalysis.Text;
using PrettyPrompt.Highlighting;

namespace Sharply.Services.SyntaxHighlighting
{
    public record HighlightedSpan(TextSpan TextSpan, AnsiColor Color);
}