// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using CSharpRepl.Services.Completion;
using CSharpRepl.Services.Extensions;
using CSharpRepl.Services.Roslyn.CustomObjectFormatters;
using CSharpRepl.Services.Theming;
using Microsoft.CodeAnalysis.Scripting.Hosting;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace CSharpRepl.Services.Roslyn;

internal sealed partial class PrettyPrinter
{
    internal IEnumerable<FormattedObject> FormatMembers(object obj, Level level, bool includeNonPublic)
    {
        foreach (var (MemberParentValue, MemberInfo) in EnumerateMembers(obj, includeNonPublic))
        {
            yield return FormatMember(MemberParentValue, MemberInfo, level);
        }
    }

    private FormattedObject FormatMember(object memberParentValue, MemberInfo member, Level level)
    {
        var memberValue = ObjectFormatterHelpers.GetMemberValue(memberParentValue, member, out var exception);

        StyledString name;
        IRenderable memberFormattedValue;
        if (exception is null)
        {
            var debuggerDisplay = ObjectFormatterHelpers.GetApplicableDebuggerDisplayAttribute(member);

            if (memberValue is null || string.IsNullOrEmpty(debuggerDisplay?.Name))
            {
                name = GetMemberDefaultName(member);
            }
            else
            {
                name = FormatWithEmbeddedExpressions(debuggerDisplay.Name, memberValue, level);
            }

            memberFormattedValue = FormatObjectSafeToRenderable(memberValue, level, quoteStringsAndCharacters: null);
        }
        else
        {
            name = GetMemberDefaultName(member);
            memberFormattedValue = GetValueRetrievalExceptionText(exception).ToParagraph();
        }

        var nameWithFormattedValue = new InlineRenderable((name + ": ").ToParagraph(), memberFormattedValue);
        return new FormattedObject(nameWithFormattedValue, memberValue);
    }

    private sealed class InlineRenderable : Renderable
    {
        private readonly IRenderable r1, r2;

        public InlineRenderable(IRenderable r1, IRenderable r2)
        {
            this.r1 = r1;
            this.r2 = r2;
        }

        protected override IEnumerable<Segment> Render(RenderOptions options, int maxWidth)
            => r1.Render(options, maxWidth).Concat(r2.Render(options, maxWidth));
    }

    private StyledString GetMemberDefaultName(MemberInfo member)
    {
        var classification = RoslynExtensions.MemberTypeToClassificationTypeName(member.MemberType);
        var prefix = AutoCompleteService.GetCompletionItemSymbolPrefix(classification, config.UseUnicode);
        var style = syntaxHighlighter.GetStyle(classification);
        return new StyledString(new StyledStringSegment[] { prefix, new StyledStringSegment(member.Name, style) });
    }

    private IEnumerable<(object MemberParentValue, MemberInfo MemberInfo)> EnumerateMembers(object obj, bool includeNonPublic)
    {
        var members = new List<MemberInfo>();

        var proxy = ObjectFormatterHelpers.GetDebuggerTypeProxy(obj);
        if (proxy != null)
        {
            includeNonPublic = false;
            obj = proxy;
        }

        var type = obj.GetType().GetTypeInfo();
        while (type != null)
        {
            members.AddRange(type.DeclaredFields.Where(f => !f.IsStatic));
            members.AddRange(type.DeclaredProperties.Where(p => p.GetMethod != null && !p.GetMethod.IsStatic));
            type = type.BaseType?.GetTypeInfo();
        }

        members.Sort((x, y) =>
        {
            // Need case-sensitive comparison here so that the order of members is
            // always well-defined (members can differ by case only). And we don't want to
            // depend on that order.
            int comparisonResult = StringComparer.OrdinalIgnoreCase.Compare(x.Name, y.Name);
            if (comparisonResult == 0)
            {
                comparisonResult = StringComparer.Ordinal.Compare(x.Name, y.Name);
            }

            return comparisonResult;
        });

        foreach (var member in members)
        {
            if (!formatter.Filter.Include(member))
            {
                continue;
            }

            bool ignoreVisibility = false;
            var browsable = member.GetCustomAttributes<DebuggerBrowsableAttribute>(false).FirstOrDefault();
            if (browsable != null)
            {
                if (browsable.State == DebuggerBrowsableState.Never)
                {
                    continue;
                }

                ignoreVisibility = true;
                if (browsable.State == DebuggerBrowsableState.RootHidden)
                {
                    var memberValue = ObjectFormatterHelpers.GetMemberValue(obj, member, out var exception);
                    if (exception != null)
                    {
                        //cannot retrieve hiddenRoot value, so we'll ignore RootHidden and show member name with exception
                        yield return (obj, member);
                    }
                    else if (memberValue != null)
                    {
                        foreach (var nestedMember in EnumerateMembers(memberValue, includeNonPublic))
                        {
                            yield return nestedMember;
                        }
                    }
                    continue;
                }
            }

            if (member is FieldInfo field)
            {
                if (!(includeNonPublic || ignoreVisibility || field.IsPublic || field.IsFamily || field.IsFamilyOrAssembly))
                {
                    continue;
                }
            }
            else
            {
                var property = (PropertyInfo)member;

                var getter = property.GetMethod;
                if (getter is null || getter.GetParameters().Length > 0)
                {
                    continue;
                }

                // If not ignoring visibility include properties that has a visible getter or setter.
                var setter = property.SetMethod;
                if (!(includeNonPublic || ignoreVisibility ||
                    getter.IsPublic || getter.IsFamily || getter.IsFamilyOrAssembly ||
                    (setter != null && (setter.IsPublic || setter.IsFamily || setter.IsFamilyOrAssembly))))
                {
                    continue;
                }
            }

            yield return (obj, member);
        }
    }

    private StyledString GetValueRetrievalExceptionText(Exception exception)
        => GetErrorText(formatter.TypeNameFormatter.FormatTypeName(exception.GetType(), formatter.GetTypeNameOptions(singleLineOptions)));

    private StyledString GetErrorText(StyledString message)
    {
        var sb = new StyledStringBuilder();
        sb.Append("!<", style: new Style(foreground: Color.Red));
        sb.Append(message);
        sb.Append('>', style: new Style(foreground: Color.Red));
        return sb.ToStyledString();
    }
}