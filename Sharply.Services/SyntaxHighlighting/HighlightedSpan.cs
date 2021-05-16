using Microsoft.CodeAnalysis.Text;
using PrettyPrompt.Highlighting;

namespace Sharply.Services.SyntaxHighlighting
{
    public class HighlightedSpan
    {
        public HighlightedSpan(TextSpan span, AnsiColor color)
        {
            TextSpan = span;
            Color = color;
        }

        public TextSpan TextSpan { get; }
        public AnsiColor Color { get; }
    }
}