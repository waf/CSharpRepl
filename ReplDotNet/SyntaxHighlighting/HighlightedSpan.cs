using Microsoft.CodeAnalysis.Text;
using PrettyPrompt.Highlighting;

namespace ReplDotNet.SyntaxHighlighting
{
    internal class HighlightedSpan
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