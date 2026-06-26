// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CSharpRepl.Services.Roslyn.Scripting;

/// <summary>
/// If the user types "var x = 0" and submits, autoinsert the trailing semicolon.
///
/// This is an <em>interactive editor</em> concern: the prompt uses it to decide whether Enter submits and to
/// show the inserted <c>;</c> on the committed line. Evaluation itself is left untouched (piped input is not
/// auto-terminated).
/// </summary>
internal static class AutoInsertSemicolon
{
    /// <summary>
    /// When <paramref name="text"/> is a single-line statement the user didn't terminate, returns it (trimmed)
    /// with the missing semicolon appended, otherwise return <see langword="null"/>
    /// </summary>
    public static string? TryAppend(string text, CSharpParseOptions parseOptions)
    {
        var trimmed = text.Trim();
        var tree = CSharpSyntaxTree.ParseText(trimmed, parseOptions);
        if (SyntaxFactory.IsCompleteSubmission(tree))
        {
            return null; // already complete — nothing to append (e.g. the expression `1 + 1`)
        }

        // Only single-line input is auto-semicoloned; a statement spread across lines (e.g. a fluent chain whose
        // cursor is at the end of line 2) is left alone so it can keep being edited.
        if (trimmed.AsSpan().IndexOfAny('\n', '\r') >= 0)
        {
            return null;
        }

        if (tree.GetRoot() is not CompilationUnitSyntax { Members: [.., FieldDeclarationSyntax { SemicolonToken.IsMissing: true }] })
        {
            return null;
        }

        var terminated = trimmed + ";";
        var fixedTree = CSharpSyntaxTree.ParseText(terminated, parseOptions);
        return SyntaxFactory.IsCompleteSubmission(fixedTree) &&
               !fixedTree.GetDiagnostics().Any(d => d.Severity == DiagnosticSeverity.Error)
            ? terminated
            : null;
    }
}
