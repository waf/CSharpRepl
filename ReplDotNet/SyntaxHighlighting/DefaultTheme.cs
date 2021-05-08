namespace ReplDotNet.SyntaxHighlighting
{
    class DefaultTheme : Theme
    {
        public DefaultTheme()
        {
            colors = new[]
            {
                new Color { name = "class name", foreground = "BrightCyan" },
                new Color { name = "struct name", foreground = "BrightCyan" },
                new Color { name = "delegate name", foreground = "BrightCyan" },
                new Color { name = "interface name", foreground = "BrightCyan" },
                new Color { name = "module name", foreground = "BrightCyan" },
                new Color { name = "record name", foreground = "BrightCyan" },
                new Color { name = "enum name", foreground = "Green" },
                new Color { name = "xml doc comment - attribute name", foreground = "Green" },
                new Color { name = "plain text", foreground = "White" },
                new Color { name = "constant name", foreground = "White" },
                new Color { name = "enum member name", foreground = "White" },
                new Color { name = "event name", foreground = "White" },
                new Color { name = "extension method name", foreground = "White" },
                new Color { name = "identifier", foreground = "White" },
                new Color { name = "label name", foreground = "White" },
                new Color { name = "local name", foreground = "White" },
                new Color { name = "method name", foreground = "White" },
                new Color { name = "property name", foreground = "White" },
                new Color { name = "namespace name", foreground = "White" },
                new Color { name = "parameter name", foreground = "White" },
                new Color { name = "number", foreground = "Blue" },
                new Color { name = "keyword - control", foreground = "BrightMagenta" },
                new Color { name = "keyword", foreground = "BrightMagenta" },
                new Color { name = "operator", foreground = "BrightMagenta" },
                new Color { name = "operator - overloaded", foreground = "BrightMagenta" },
                new Color { name = "preprocessor keyword", foreground = "BrightMagenta" },
                new Color { name = "string - escape character", foreground = "BrightMagenta" },
                new Color { name = "string - verbatim", foreground = "BrightYellow" },
                new Color { name = "string", foreground = "BrightYellow" },
                new Color { name = "xml doc comment - attribute quotes", foreground = "BrightYellow" },
                new Color { name = "xml doc comment - attribute value", foreground = "BrightYellow" },
                new Color { name = "type parameter name", foreground = "Yellow" },
                new Color { name = "comment", foreground = "Cyan" },
                new Color { name = "xml doc comment - cdata section", foreground = "Cyan" },
                new Color { name = "xml doc comment - comment", foreground = "Cyan" },
                new Color { name = "xml doc comment - delimiter", foreground = "Cyan" },
                new Color { name = "xml doc comment - entity reference", foreground = "Cyan" },
                new Color { name = "xml doc comment - name", foreground = "Cyan" },
                new Color { name = "xml doc comment - processing instruction", foreground = "Cyan" },
                new Color { name = "xml doc comment - text", foreground = "Cyan" }
            };
        }
    }

    public class Theme
    {
        public Color[] colors { get; set; }
    }

    public class Color
    {
        public string name { get; set; }
        public string foreground { get; set; }
    }

}
