// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Linq;
using System.Reflection;
using CSharpRepl.Services.Theming;
using Microsoft.CodeAnalysis.Classification;

namespace CSharpRepl.Services.Roslyn.Formatting.CustomObjectFormatters;

internal class MethodInfoFormatter : CustomObjectFormatter<MethodInfo>
{
    public static readonly MethodInfoFormatter Instance = new();

    private MethodInfoFormatter() { }

    public override StyledString Format(MethodInfo value, Level level, Formatter formatter)
    {
        var methodNameStyle = formatter.GetStyle(ClassificationTypeNames.MethodName);
        var typeFormatter = TypeFormatter.Instance;

        var sb = new StyledStringBuilder();

        //modifiers
        if (level is Level.FirstDetailed or Level.FirstSimple)
        {
            var modifiers = string.Join(" ", ReflectionHelpers.GetModifiers(value));
            sb.Append(modifiers, formatter.KeywordStyle)
              .Append(' ');
        }

        //return type
        if (level < Level.ThirdPlus)
        {
            AppendReturnType(level is Level.FirstDetailed ? level : level.Increment()).Append(' ');
        }

        //name
        string name;
        if (level is Level.FirstDetailed)
        {
            name = value.Name;
        }
        else
        {
            var nameParts = value.Name.Split('.');
            name = string.Join(".", nameParts.TakeLast(level is Level.FirstSimple ? 2 : 1)); //"interface.method" or "method" without namespace
        }
        sb.Append(name, methodNameStyle);

        if (level < Level.ThirdPlus)
        {
            //generic arguments
            if (value.IsGenericMethod)
            {
                sb.Append('<');
                foreach (var a in value.GetGenericArguments())
                {
                    sb.Append(typeFormatter.Format(a, level, formatter));
                }
                sb.Append('>');
            }

            //parameters
            sb.Append('(');
            var parameters = value.GetParameters();
            for (int i = 0; i < parameters.Length; i++)
            {
                var p = parameters[i];
                sb.Append(typeFormatter.Format(p.ParameterType, level, formatter));

                if (level < Level.Second)
                {
                    sb.Append(' ').Append(p.Name);
                }

                if (i != parameters.Length - 1) sb.Append(", ");
            }
            sb.Append(')');
        }

        return sb.ToStyledString();

        StyledStringBuilder AppendReturnType(Level level)
        {
            return
                value.ReturnType == typeof(void) ?
                sb.Append(new StyledString("void", formatter.KeywordStyle)) :
                sb.Append(typeFormatter.Format(value.ReturnType, level, formatter));
        }
    }
}