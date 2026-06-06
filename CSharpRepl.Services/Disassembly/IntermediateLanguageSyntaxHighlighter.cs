// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using CSharpRepl.Services.SyntaxHighlighting;
using Microsoft.CodeAnalysis.Classification;
using Microsoft.CodeAnalysis.Text;
using PrettyPrompt.Highlighting;

namespace CSharpRepl.Services.Disassembly;

/// <summary>
/// A syntax highlighter for ILSpy's IL text output. It produces <see cref="FormatSpan"/>s colored from the active theme.
/// It reuses the same classification names as the C# highlighter, so IL and C# share the same color theme.
///
/// It deliberately leaves the embedded C# source comments alone — their character ranges are passed in as
/// <paramref name="protectedRegions"/> and highlighted separately by the real Roslyn classifier.
/// </summary>
internal static partial class IntermediateLanguageSyntaxHighlighter
{
    private static readonly Regex Token = TokenRegex();
    private static readonly Regex StringLiteral = StringLiteralRegex();
    private static readonly Regex Label = LabelRegex();
    private static readonly Regex Number = NumberRegex();

    // Common IL keywords: primitive types, calling conventions, and type/member modifiers. Opcodes are not
    // listed here — the first bare word on an instruction line (the one after the IL_xxxx label) is treated
    // as the opcode mnemonic.
    private static readonly HashSet<string> Keywords = new(StringComparer.Ordinal)
    {
        "void", "bool", "char", "object", "string", "class", "valuetype", "typedref",
        "int", "int8", "int16", "int32", "int64", "uint8", "uint16", "uint32", "uint64",
        "float32", "float64", "native", "nativeint", "unsigned",
        "instance", "explicit", "static", "vararg", "cil", "managed", "unmanaged", "pinned",
        "public", "private", "family", "assembly", "famandassem", "famorassem", "privatescope",
        "extends", "implements", "auto", "sequential", "ansi", "unicode", "autochar",
        "sealed", "abstract", "beforefieldinit", "specialname", "rtspecialname", "initonly",
        "literal", "modreq", "modopt", "nested", "hidebysig", "newslot", "virtual", "final",
        "serializable", "import", "default", "init", "out", "in", "opt", "to", "at",
    };

    public static IReadOnlyList<FormatSpan> Highlight(string il, SyntaxHighlighter theme, IReadOnlyList<TextSpan> protectedRegions)
    {
        var spans = new List<FormatSpan>();

        void Add(int start, int length, string classification)
        {
            if (length > 0 && theme.TryGetAnsiColor(classification, out var color))
            {
                spans.Add(new FormatSpan(start, length, new ConsoleFormat(Foreground: color)));
            }
        }

        var lineStart = 0;
        foreach (var line in il.Split('\n'))
        {
            HighlightLine(line, lineStart, protectedRegions, Add);
            lineStart += line.Length + 1; // +1 for the '\n' separator that join restored
        }

        // returned unsorted: the caller merges these with the embedded-C# spans and sorts the combined set.
        return spans;
    }

    private static void HighlightLine(string line, int lineStart, IReadOnlyList<TextSpan> protectedRegions, Action<int, int, string> add)
    {
        // 1. a "// ..." comment runs to the end of the line. Color it, but clip so we never paint over an
        //    embedded C# snippet (those are highlighted by Roslyn instead).
        var commentIndex = FindCommentStart(line);
        if (commentIndex >= 0)
        {
            var commentStart = lineStart + commentIndex;
            var commentLength = ClipToProtected(commentStart, line.Length - commentIndex, protectedRegions);
            add(commentStart, commentLength, ClassificationTypeNames.Comment);
        }

        var codeEnd = commentIndex < 0 ? line.Length : commentIndex;
        if (codeEnd == 0)
        {
            return;
        }

        var code = line[..codeEnd];

        // 2. string literals get their own color; mask them so the tokenizer doesn't split them on spaces.
        var masked = code.ToCharArray();
        foreach (Match s in StringLiteral.Matches(code))
        {
            add(lineStart + s.Index, s.Length, ClassificationTypeNames.StringLiteral);
            for (var i = s.Index; i < s.Index + s.Length; i++)
            {
                masked[i] = ' ';
            }
        }
        var tokenized = new string(masked);

        // 3. tokenize the remainder and classify each token.
        var tokens = Token.Matches(tokenized);
        var isInstruction = tokens.Count > 0 && Label.IsMatch(tokens[0].Value);
        for (var k = 0; k < tokens.Count; k++)
        {
            var token = tokens[k].Value;
            var start = lineStart + tokens[k].Index;

            if (k == 0 && isInstruction)
            {
                add(start, token.Length, ClassificationTypeNames.LabelName);
            }
            else if (k == 1 && isInstruction)
            {
                // the opcode mnemonic, e.g. "ldc.i4.5" or "callvirt".
                add(start, token.Length, ClassificationTypeNames.Keyword);
            }
            else if (token[0] == '.')
            {
                add(start, token.Length, ClassificationTypeNames.ControlKeyword);
            }
            else if (token.Contains("::", StringComparison.Ordinal))
            {
                HighlightMemberReference(token, start, add);
            }
            else
            {
                var (coreStart, core) = StripWrappers(token, start);
                if (Keywords.Contains(core))
                {
                    add(coreStart, core.Length, ClassificationTypeNames.Keyword);
                }
                else if (Number.IsMatch(core))
                {
                    add(coreStart, core.Length, ClassificationTypeNames.NumericLiteral);
                }
            }
        }
    }

    /// <summary>Colors the type and method name within a reference like <c>[Asm]Some.Type::Method(int32)</c>.</summary>
    private static void HighlightMemberReference(string token, int tokenStart, Action<int, int, string> add)
    {
        var separator = token.IndexOf("::", StringComparison.Ordinal);
        if (separator < 0)
        {
            return;
        }

        // type name: the segment between the assembly reference's closing ']' (if any) and "::".
        var typeStart = token.LastIndexOf(']', separator) + 1;
        if (typeStart < separator)
        {
            add(tokenStart + typeStart, separator - typeStart, ClassificationTypeNames.ClassName);
        }

        // method name: after "::" up to the opening parenthesis (or end of token).
        var methodStart = separator + 2;
        var parenthesis = token.IndexOf('(', methodStart);
        var methodEnd = parenthesis < 0 ? token.Length : parenthesis;
        add(tokenStart + methodStart, methodEnd - methodStart, ClassificationTypeNames.MethodName);
    }

    /// <summary>Strips surrounding brackets/punctuation (e.g. <c>[0]</c> → <c>0</c>, <c>int32,</c> → <c>int32</c>).</summary>
    private static (int Start, string Core) StripWrappers(string token, int tokenStart)
    {
        var start = 0;
        var end = token.Length;
        while (start < end && (token[start] is '(' or '['))
        {
            start++;
        }
        while (end > start && (token[end - 1] is ')' or ']' or ',' or ';'))
        {
            end--;
        }
        return (tokenStart + start, token[start..end]);
    }

    private static int FindCommentStart(string line)
    {
        var inString = false;
        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];
            if (inString)
            {
                if (c == '\\')
                {
                    i++; // skip the escaped character
                }
                else if (c == '"')
                {
                    inString = false;
                }
            }
            else if (c == '"')
            {
                inString = true;
            }
            else if (c == '/' && i + 1 < line.Length && line[i + 1] == '/')
            {
                return i;
            }
        }
        return -1;
    }

    /// <summary>Shortens a span so it stops before the first protected (C#) region it would otherwise enter.</summary>
    private static int ClipToProtected(int start, int length, IReadOnlyList<TextSpan> protectedRegions)
    {
        var end = start + length;
        foreach (var region in protectedRegions)
        {
            if (region.Start >= start && region.Start < end)
            {
                end = region.Start;
            }
        }
        return end - start;
    }

    [GeneratedRegex(@"\S+")]
    private static partial Regex TokenRegex();

    [GeneratedRegex("\"(?:\\\\.|[^\"\\\\])*\"")]
    private static partial Regex StringLiteralRegex();

    [GeneratedRegex("^IL_[0-9A-Fa-f]+:?$")]
    private static partial Regex LabelRegex();

    [GeneratedRegex(@"^(?:0x[0-9A-Fa-f]+|-?\d+)$")]
    private static partial Regex NumberRegex();
}
