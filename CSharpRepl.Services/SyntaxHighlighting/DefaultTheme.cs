// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Microsoft.CodeAnalysis.Classification;

namespace CSharpRepl.Services.SyntaxHighlighting
{
    class DefaultTheme : Theme
    {
        public DefaultTheme()
        {
            colors = new[]
            {
                new Color { name = ClassificationTypeNames.ClassName, foreground = "BrightCyan" },
                new Color { name = ClassificationTypeNames.StructName, foreground = "BrightCyan" },
                new Color { name = ClassificationTypeNames.DelegateName, foreground = "BrightCyan" },
                new Color { name = ClassificationTypeNames.InterfaceName, foreground = "BrightCyan" },
                new Color { name = ClassificationTypeNames.ModuleName, foreground = "BrightCyan" },
                new Color { name = ClassificationTypeNames.RecordClassName, foreground = "BrightCyan" },
                new Color { name = "record struct name", foreground = "BrightCyan" },
                new Color { name = ClassificationTypeNames.EnumName, foreground = "Green" },
                new Color { name = ClassificationTypeNames.Text, foreground = "White" },
                new Color { name = ClassificationTypeNames.ConstantName, foreground = "White" },
                new Color { name = ClassificationTypeNames.EnumMemberName, foreground = "White" },
                new Color { name = ClassificationTypeNames.EventName, foreground = "White" },
                new Color { name = ClassificationTypeNames.ExtensionMethodName, foreground = "White" },
                new Color { name = ClassificationTypeNames.Identifier, foreground = "White" },
                new Color { name = ClassificationTypeNames.LabelName, foreground = "White" },
                new Color { name = ClassificationTypeNames.LocalName, foreground = "White" },
                new Color { name = ClassificationTypeNames.MethodName, foreground = "White" },
                new Color { name = ClassificationTypeNames.PropertyName, foreground = "White" },
                new Color { name = ClassificationTypeNames.NamespaceName, foreground = "White" },
                new Color { name = ClassificationTypeNames.ParameterName, foreground = "White" },
                new Color { name = ClassificationTypeNames.NumericLiteral, foreground = "Blue" },
                new Color { name = ClassificationTypeNames.ControlKeyword, foreground = "BrightMagenta" },
                new Color { name = ClassificationTypeNames.Keyword, foreground = "BrightMagenta" },
                new Color { name = ClassificationTypeNames.Operator, foreground = "BrightMagenta" },
                new Color { name = ClassificationTypeNames.OperatorOverloaded, foreground = "BrightMagenta" },
                new Color { name = ClassificationTypeNames.PreprocessorKeyword, foreground = "BrightMagenta" },
                new Color { name = ClassificationTypeNames.StringEscapeCharacter, foreground = "BrightMagenta" },
                new Color { name = ClassificationTypeNames.VerbatimStringLiteral, foreground = "BrightYellow" },
                new Color { name = ClassificationTypeNames.StringLiteral, foreground = "BrightYellow" },
                new Color { name = ClassificationTypeNames.TypeParameterName, foreground = "Yellow" },
                new Color { name = ClassificationTypeNames.Comment, foreground = "Cyan" },
                new Color { name = ClassificationTypeNames.XmlDocCommentAttributeQuotes, foreground = "Green" },
                new Color { name = ClassificationTypeNames.XmlDocCommentAttributeValue, foreground = "Green" },
                new Color { name = ClassificationTypeNames.XmlDocCommentAttributeName, foreground = "Cyan" },
                new Color { name = ClassificationTypeNames.XmlDocCommentCDataSection, foreground = "Cyan" },
                new Color { name = ClassificationTypeNames.XmlDocCommentComment, foreground = "Cyan" },
                new Color { name = ClassificationTypeNames.XmlDocCommentDelimiter, foreground = "Cyan" },
                new Color { name = ClassificationTypeNames.XmlDocCommentEntityReference, foreground = "Cyan" },
                new Color { name = ClassificationTypeNames.XmlDocCommentName, foreground = "Cyan" },
                new Color { name = ClassificationTypeNames.XmlDocCommentProcessingInstruction, foreground = "Cyan" },
                new Color { name = ClassificationTypeNames.XmlDocCommentText, foreground = "Cyan" }
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
