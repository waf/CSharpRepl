// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Linq;
using System.Reflection;
using CSharpRepl.Services.Theming;
using Microsoft.CodeAnalysis.Classification;
using Microsoft.CodeAnalysis.Scripting.Hosting;

namespace CSharpRepl.Services.Roslyn.CustomObjectFormatters;

internal class MethodInfoFormatter : CustomObjectFormatter<MethodInfo>
{
    public static readonly MethodInfoFormatter Instance = new();

    private MethodInfoFormatter() { }

    public override StyledString Format(MethodInfo value, int level, CommonObjectFormatter.Visitor visitor)
    {
        var methodNameStyle = visitor.SyntaxHighlighter.GetStyle(ClassificationTypeNames.MethodName);
        var typeFormatter = TypeFormatter.InstanceWithForcedUsageOfLanguageKeywords;

        var sb = new StyledStringBuilder();
        if (level == 0)
        {
            var modifiers = string.Join(" ", ReflectionHelpers.GetModifiers(value));
            sb.Append(modifiers, visitor.SyntaxHighlighter.KeywordStyle)
              .Append(' ');
            AppendReturnType(level + 1).Append(' ');

            sb.Append(value.Name, methodNameStyle);

            if (value.IsGenericMethod)
            {
                sb.Append('<');
                foreach (var a in value.GetGenericArguments())
                {
                    sb.Append(typeFormatter.Format(a, level, visitor));
                }
                sb.Append('>');
            }

            sb.Append('(');
            var parameters = value.GetParameters();
            for (int i = 0; i < parameters.Length; i++)
            {
                var p = parameters[i];
                sb.Append(typeFormatter.Format(p.ParameterType, level, visitor))
                  .Append(' ')
                  .Append(p.Name);
                if (i != parameters.Length - 1) sb.Append(", ");
            }
            sb.Append(')');
        }
        else if (level == 1)
        {
            AppendReturnType(level + 1).Append(' ');

            sb.Append(value.Name.Split('.').Last(), methodNameStyle);

            if (value.IsGenericMethod)
            {
                sb.Append('<');
                foreach (var a in value.GetGenericArguments())
                {
                    sb.Append(typeFormatter.Format(a, level + 1, visitor));
                }
                sb.Append('>');
            }

            sb.Append('(');
            var parameters = value.GetParameters();
            for (int i = 0; i < parameters.Length; i++)
            {
                var p = parameters[i];
                sb.Append(typeFormatter.Format(p.ParameterType, level + 1, visitor));
                if (i != parameters.Length - 1) sb.Append(", ");
            }
            sb.Append(')');
        }
        else
        {
            sb.Append(value.Name.Split('.').Last(), methodNameStyle);
        }

        return sb.ToStyledString();

        StyledStringBuilder AppendReturnType(int level)
        {
            return
                value.ReturnType == typeof(void) ?
                sb.Append(new StyledString("void", visitor.SyntaxHighlighter.KeywordStyle)) :
                sb.Append(typeFormatter.Format(value.ReturnType, level, visitor));
        }
    }
}