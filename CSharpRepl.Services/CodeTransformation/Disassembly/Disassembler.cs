// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using CSharpRepl.Services.Roslyn.Scripting;
using ICSharpCode.Decompiler;
using ICSharpCode.Decompiler.DebugInfo;
using ICSharpCode.Decompiler.Disassembler;
using ICSharpCode.Decompiler.Metadata;
using Microsoft.CodeAnalysis.Text;

namespace CSharpRepl.Services.CodeTransformation.Disassembly;

/// <summary>
/// Renders a compiled assembly as IL, interleaving each statement's IL with the C# source it came from. The compile
/// step lives in <see cref="CodeTransformer"/>; this class only turns the compiled assembly into annotated IL.
/// </summary>
internal partial class Disassembler
{
    // Roslyn marks "hidden" sequence points (compiler plumbing with no corresponding source) with this line number.
    private const int HiddenSequencePointLine = 0xFEEFEE;
    private static readonly Regex SequencePointPattern = SequencePointRegex();

    /// <param name="compilation">A successful compilation; a portable PDB must have been emitted (for sequence points).</param>
    /// <returns>
    /// The disassembled IL, plus the character ranges within that IL text of the embedded C# source snippets, so a
    /// caller can highlight them with the C# classifier.
    /// </returns>
    public (EvaluationResult Result, IReadOnlyList<TextSpan> CommentSpans) Render(CompilationResult compilation)
    {
        using var file = new PEFile(Guid.NewGuid().ToString(), compilation.AssemblyStream, PEStreamOptions.LeaveOpen);
        using var debugInfo = new PortablePdbDebugInfoProvider(compilation.PdbStream!);
        var ilCodeOutput = DisassembleFile(file, debugInfo);

        // ILSpy emits "// sequence point: (line, col)..." markers next to the IL each statement produced. Replace
        // those with the actual C# source line so the IL reads as mixed IL + C#, recording where each snippet lands
        // so the caller can syntax-highlight it.
        var sourceLines = compilation.Code.Replace("\r\n", "\n").Split('\n');
        var rawLines = ilCodeOutput.ToString().Split(["\r\n", "\n"], StringSplitOptions.None);
        var builder = new StringBuilder();
        var commentSpans = new List<TextSpan>();
        foreach (var rawLine in rawLines)
        {
            if (builder.Length > 0)
            {
                builder.Append('\n');
            }
            var (text, snippetStart, snippetLength) = AnnotateSequencePoint(rawLine.TrimEnd(), sourceLines);
            if (snippetLength > 0)
            {
                commentSpans.Add(new TextSpan(builder.Length + snippetStart, snippetLength));
            }
            builder.Append(text);
        }
        builder.Append(string.Join('\n', compilation.Footer));

        return (new EvaluationResult.Success(compilation.Code, builder.ToString(), []), commentSpans);
    }

    /// <summary>
    /// Turns an ILSpy "// sequence point: (line, col) to (line, col)" comment into the actual C# source it maps to.
    /// </summary>
    private static (string Text, int SnippetStart, int SnippetLength) AnnotateSequencePoint(string line, string[] sourceLines)
    {
        var match = SequencePointPattern.Match(line);
        if (!match.Success)
        {
            return (line, 0, 0);
        }

        var indent = match.Groups["indent"].Value;
        var startLine = int.Parse(match.Groups["sl"].Value);
        if (startLine == HiddenSequencePointLine)
        {
            return ($"{indent}// (compiler-generated, no source)", 0, 0);
        }

        var source = ExtractSource(
            sourceLines,
            startLine,
            int.Parse(match.Groups["sc"].Value),
            int.Parse(match.Groups["el"].Value),
            int.Parse(match.Groups["ec"].Value));

        // if we couldn't resolve the span for any reason, leave the original marker rather than lose information.
        if (source is null)
        {
            return (line, 0, 0);
        }

        // the snippet sits after the indentation and the "// " comment prefix.
        return ($"{indent}// {source}", indent.Length + 3, source.Length);
    }

    /// <summary>
    /// Extracts the substring of the source spanning the given 1-based line/column range, collapsed onto a single line.
    /// </summary>
    private static string? ExtractSource(string[] sourceLines, int startLine, int startColumn, int endLine, int endColumn)
    {
        if (startLine < 1 || endLine > sourceLines.Length || startLine > endLine)
        {
            return null;
        }

        if (startLine == endLine)
        {
            var text = sourceLines[startLine - 1];
            var start = Math.Clamp(startColumn - 1, 0, text.Length);
            var end = Math.Clamp(endColumn - 1, start, text.Length);
            return text[start..end].Trim();
        }

        var builder = new StringBuilder();
        for (var i = startLine; i <= endLine; i++)
        {
            var text = sourceLines[i - 1];
            if (i == startLine)
            {
                text = text[Math.Clamp(startColumn - 1, 0, text.Length)..];
            }
            else if (i == endLine)
            {
                text = text[..Math.Clamp(endColumn - 1, 0, text.Length)];
            }

            if (builder.Length > 0)
            {
                builder.Append(' ');
            }
            builder.Append(text.Trim());
        }
        return builder.ToString().Trim();
    }

    private static PlainTextOutput DisassembleFile(PEFile file, IDebugInfoProvider debugInfo)
    {
        // the disassemblers will write to the ilCodeOutput variable when invoked.
        var ilCodeOutput = new PlainTextOutput { IndentationString = new string(' ', 4) };

        // if the user provides a simple statement (e.g. like "Console.WriteLine(5);") our goal is to return
        // only the IL that corresponds to this exact input without any generated enclosing Program or Main method classes.

        // if we find any additional types, fallback to disassembling and returning the entire file.
        var asm = file.Reader;
        var asmReader = asm.GetMetadataReader();
        var definedTypes = asmReader.TypeDefinitions.ToArray();
        var definedTypeNames = definedTypes.Select(t => asmReader.GetString(asmReader.GetTypeDefinition(t).Name)).ToArray();
        if (definedTypeNames.Except(["<Module>", "Program", "RefSafetyRulesAttribute", "EmbeddedAttribute"]).Any())
        {
            return DisassembleAll(file, ilCodeOutput, debugInfo);
        }

        // if we find any additional methods, fallback to disassembling and returning the entire file.
        var programTypeIndex = Array.IndexOf(definedTypeNames, "Program");
        if (programTypeIndex == -1)
        {
            return DisassembleAll(file, ilCodeOutput, debugInfo);
        }
        var programType = asmReader.GetTypeDefinition(definedTypes[programTypeIndex]);
        var methods = programType.GetMethods().ToArray();
        var methodNames = methods.Select(m => asmReader.GetString(asmReader.GetMethodDefinition(m).Name)).ToArray();
        if (methodNames.Except(["<Main>$", ".ctor"]).Any())
        {
            return DisassembleAll(file, ilCodeOutput, debugInfo);
        }

        // we successfully found that there's only a simple Program.Main, so disassemble just the Main method body.
        new MethodBodyDisassembler(ilCodeOutput, CancellationToken.None) { ShowSequencePoints = true, DebugInfo = debugInfo }
            .Disassemble(file, methods[Array.IndexOf(methodNames, "<Main>$")]);
        return ilCodeOutput;

        static PlainTextOutput DisassembleAll(PEFile file, PlainTextOutput ilCodeOutput, IDebugInfoProvider debugInfo)
        {
            new ReflectionDisassembler(ilCodeOutput, CancellationToken.None) { ShowSequencePoints = true, DebugInfo = debugInfo }
                .WriteModuleContents(file); // writes to the "ilCodeOutput" variable
            return ilCodeOutput;
        }
    }

    [GeneratedRegex(@"^(?<indent>\s*)// sequence point: \(line (?<sl>\d+), col (?<sc>\d+)\) to \(line (?<el>\d+), col (?<ec>\d+)\).*$")]
    private static partial Regex SequencePointRegex();
}
