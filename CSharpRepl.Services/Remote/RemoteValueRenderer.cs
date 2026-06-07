// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using CSharpRepl.Inspector.Contracts;
using CSharpRepl.Services.Roslyn.Formatting;
using CSharpRepl.Services.SyntaxHighlighting;
using Microsoft.CodeAnalysis.Classification;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace CSharpRepl.Services.Remote;

/// <summary>
/// Renders a <see cref="RemoteValue"/> / <see cref="RemoteException"/> produced by the inspector (in the
/// target process) into a themed Spectre <see cref="IRenderable"/> on the controller. The engine carries
/// theme-agnostic data (formatted scalar text + a <see cref="RemoteValueStyle"/> hint, or a structured
/// member/element breakdown); this applies the user's theme via the controller's <see cref="SyntaxHighlighter"/>,
/// mirroring the local REPL's simple-vs-detailed behavior (a one-line summary at the simple level, a member
/// <see cref="Tree"/> at the detailed level).
/// </summary>
internal sealed class RemoteValueRenderer
{
    // The engine already caps members/items at 100; the controller renders what arrived and appends an
    // ellipsis when the engine flagged truncation.
    private const int MaxInlineItems = 10;

    private readonly SyntaxHighlighter highlighter;

    public RemoteValueRenderer(SyntaxHighlighter highlighter) => this.highlighter = highlighter;

    public IRenderable Render(RemoteValue value, Level level) => value.Kind switch
    {
        RemoteValueKind.Null => Styled("null", RemoteValueStyle.Keyword),
        RemoteValueKind.Scalar => Styled(value.DisplayText, value.Style),
        RemoteValueKind.Collection => RenderCollection(value, level),
        RemoteValueKind.Object => RenderObject(value, level),
        _ => Styled(value.DisplayText, value.Style),
    };

    /// <summary>Renders a remote exception as a red-bordered panel, mirroring the local REPL's error rendering.</summary>
    public (IRenderable Renderable, string PlainText) RenderException(RemoteException exception, Level level)
    {
        // Compile errors carry their diagnostics in Message and have no useful stack trace; runtime errors
        // show the message at the simple level and the full ToString (stack trace) at the detailed level.
        var isCompilationError = exception.TypeName == "CompilationError";
        var body = (isCompilationError || level != Level.FirstDetailed)
            ? exception.Message
            : (string.IsNullOrEmpty(exception.Detail) ? exception.Message : exception.Detail);

        var panel = new Panel(new Paragraph(body))
        {
            Header = new PanelHeader(ShortTypeName(exception.TypeName), Justify.Center),
            BorderStyle = new Style(foreground: Color.Red),
        };
        return (panel, body);
    }

    private IRenderable RenderObject(RemoteValue value, Level level)
    {
        var header = Styled(value.DisplayText, value.Style);

        if (level != Level.FirstDetailed || value.Members is not { Count: > 0 } members)
            return header;

        var tree = new Tree(header);
        foreach (var member in members)
        {
            var line = new Paragraph();
            line.Append(member.Name, MemberStyle);
            line.Append(": ");
            AppendInline(line, member.Value);
            tree.AddNode(line);
        }
        if (value.Truncated)
            tree.AddNode(new Paragraph("..."));

        return tree;
    }

    private IRenderable RenderCollection(RemoteValue value, Level level)
    {
        if (level != Level.FirstDetailed || value.Items is not { Count: > 0 } items)
            return CollectionInline(value);

        var tree = new Tree(CollectionHeader(value));
        foreach (var item in items)
        {
            var line = new Paragraph();
            AppendInline(line, item);
            tree.AddNode(line);
        }
        if (value.Truncated)
            tree.AddNode(new Paragraph("..."));

        return tree;
    }

    /// <summary>Builds the <c>List&lt;int&gt;(3)</c> style header for a collection.</summary>
    private Paragraph CollectionHeader(RemoteValue value)
    {
        var header = new Paragraph();
        header.Append(value.TypeName ?? "collection", TypeNameStyle);
        if (value.Count is { } count)
        {
            header.Append("(");
            header.Append(count.ToString(), NumberStyle);
            header.Append(")");
        }
        return header;
    }

    /// <summary>Builds the <c>List&lt;int&gt;(3) { 1, 2, 3 }</c> one-line summary for the simple level.</summary>
    private Paragraph CollectionInline(RemoteValue value)
    {
        var line = CollectionHeader(value);
        line.Append(" { ");

        var items = value.Items;
        if (items is { Count: > 0 })
        {
            var shown = Math.Min(items.Count, MaxInlineItems);
            for (var i = 0; i < shown; i++)
            {
                if (i > 0) line.Append(", ");
                AppendInline(line, items[i]);
            }
            if (items.Count > shown || value.Truncated)
                line.Append(", ...");
        }
        else if (value.Truncated)
        {
            line.Append("...");
        }

        line.Append(" }");
        return line;
    }

    /// <summary>Appends a single-line representation of <paramref name="value"/> to <paramref name="line"/>.</summary>
    private void AppendInline(Paragraph line, RemoteValue value)
    {
        switch (value.Kind)
        {
            case RemoteValueKind.Null:
                line.Append("null", KeywordStyle);
                break;
            case RemoteValueKind.Collection:
                AppendCollectionInline(line, value);
                break;
            default: // Scalar / Object — the engine already produced the one-line DisplayText.
                line.Append(value.DisplayText, StyleFor(value.Style));
                break;
        }
    }

    private void AppendCollectionInline(Paragraph line, RemoteValue value)
    {
        line.Append(value.TypeName ?? "collection", TypeNameStyle);
        if (value.Count is { } count)
        {
            line.Append("(");
            line.Append(count.ToString(), NumberStyle);
            line.Append(")");
        }
    }

    private Paragraph Styled(string text, RemoteValueStyle style) => new Paragraph().Append(text, StyleFor(style));

    private Style? StyleFor(RemoteValueStyle style) => style switch
    {
        RemoteValueStyle.Number => NumberStyle,
        RemoteValueStyle.String => StringStyle,
        RemoteValueStyle.Keyword => KeywordStyle,
        RemoteValueStyle.TypeName => TypeNameStyle,
        _ => (Style?)null,
    };

    private Style NumberStyle => highlighter.GetStyle(ClassificationTypeNames.NumericLiteral);
    private Style StringStyle => highlighter.GetStyle(ClassificationTypeNames.StringLiteral);
    private Style KeywordStyle => highlighter.KeywordStyle;
    private Style TypeNameStyle => highlighter.GetStyle(ClassificationTypeNames.ClassName);
    private Style MemberStyle => highlighter.GetStyle(ClassificationTypeNames.PropertyName);

    private static string ShortTypeName(string fullName)
    {
        var lastDot = fullName.LastIndexOf('.');
        return lastDot >= 0 && lastDot < fullName.Length - 1 ? fullName[(lastDot + 1)..] : fullName;
    }
}
