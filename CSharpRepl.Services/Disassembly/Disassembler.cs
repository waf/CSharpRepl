// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using CSharpRepl.Services.Extensions;
using CSharpRepl.Services.Roslyn.References;
using CSharpRepl.Services.Roslyn.Scripting;
using ICSharpCode.Decompiler;
using ICSharpCode.Decompiler.DebugInfo;
using ICSharpCode.Decompiler.Disassembler;
using ICSharpCode.Decompiler.Metadata;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;
using Microsoft.CodeAnalysis.Text;

namespace CSharpRepl.Services.Disassembly;

/// <summary>
/// Shows the IL code for the user's C# code.
/// </summary>
internal partial class Disassembler
{
    // Roslyn marks "hidden" sequence points (compiler plumbing with no corresponding source) with this line number.
    private const int HiddenSequencePointLine = 0xFEEFEE;
    private static readonly Regex SequencePointPattern = SequencePointRegex();

    private readonly AssemblyReferenceService referenceService;
    private readonly (string name, CompileDelegate compile)[] compilers;
    private readonly CSharpParseOptions parseOptions;
    private readonly CSharpCompilationOptions compilationOptions;

    public Disassembler(CSharpParseOptions parseOptions, CSharpCompilationOptions compilationOptions, AssemblyReferenceService referenceService, ScriptRunner scriptRunner)
    {
        this.parseOptions = parseOptions.WithKind(SourceCodeKind.Regular);
        this.compilationOptions = compilationOptions;
        this.referenceService = referenceService;

        // we will try to compile the user's code several different ways. The first one that succeeds will be used.
        this.compilers =
        [
            // "console application" will work for standalone statements, due to C#'s top-level statement feature.
            (name: "Console Application (with top-level statements)",
             compile: (code, optimizationLevel) => Compile(code, optimizationLevel, OutputKind.ConsoleApplication)),
            // "DLL" will work if the user doesn't have statements, for example, they're only defining types.
            (name: "DLL",
             compile: (code, optimizationLevel) => Compile(code, optimizationLevel, OutputKind.DynamicallyLinkedLibrary)),
            // Compiling as a script will work for most other cases, but it's quite verbose so we use it as a last resort.
            (name: "Scripting session (will be overly verbose)",
             compile: scriptRunner.CompileTransient)
        ];
    }

    /// <returns>
    /// The disassembled IL, plus (on success) the character ranges within that IL text of the embedded C# source
    /// snippets, so a caller can highlight them with the C# classifier. The ranges are empty on failure.
    /// </returns>
    public (EvaluationResult Result, IReadOnlyList<TextSpan> CommentSpans) Disassemble(string code, bool debugMode)
    {
        var usings = referenceService.Usings.Select(u => u.NormalizeWhitespace().ToString());
        code = string.Join(Environment.NewLine, usings) + Environment.NewLine + code;

        var optimizationLevel = debugMode ? OptimizationLevel.Debug : OptimizationLevel.Release;
        var commentFooter = new List<string>
        {
            $"// Disassembled in {optimizationLevel} Mode."
            + (optimizationLevel == OptimizationLevel.Debug
                ? " Press Ctrl+F9 to disassemble in Release Mode."
                : string.Empty)
        };

        using var stream = new MemoryStream();
        using var pdbStream = new MemoryStream();

        // emit a portable PDB alongside the assembly so we can annotate the IL with the
        // C# source line each instruction came from (the "mixed IL + C#" view).
        var emitOptions = new EmitOptions(debugInformationFormat: DebugInformationFormat.PortablePdb);

        foreach (var (name, compile) in compilers)
        {
            // step 1, try to compile the input using one of our compiler configurations.
            stream.SetLength(0);
            pdbStream.SetLength(0);
            var compiled = compile(code, optimizationLevel);
            var compilationResult = compiled.Emit(stream, pdbStream: pdbStream, options: emitOptions);

            // step 2, disassemble the compiled result and return the IL
            if (compilationResult.Success)
            {
                commentFooter.Add($"// Compiling code as {name}: succeeded.");
                stream.Position = 0;
                pdbStream.Position = 0;
                var file = new PEFile(Guid.NewGuid().ToString(), stream, PEStreamOptions.LeaveOpen);

                using var debugInfo = new PortablePdbDebugInfoProvider(pdbStream);
                var ilCodeOutput = DisassembleFile(file, debugInfo);

                // ILSpy emits "// sequence point: (line, col)..." markers next to the IL each statement
                // produced. Replace those with the actual C# source line so the IL reads as mixed IL + C#,
                // recording where each snippet lands so the caller can syntax-highlight it.
                var sourceLines = code.Replace("\r\n", "\n").Split('\n');
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
                builder.Append(string.Join('\n', commentFooter));

                return (new EvaluationResult.Success(code, builder.ToString(), []), commentSpans);
            }

            // failure, we couldn't compile it, move on to the next compiler configuration.
            commentFooter.Add($"// Compiling code as {name}: failed.");
            commentFooter.AddRange(compilationResult
                .Diagnostics
                .Where(d => d.Severity == DiagnosticSeverity.Error)
                .Select(err => "//   - " + err.GetMessage())
            );
        }

        return (
            new EvaluationResult.Error(
                new InvalidOperationException("// Could not compile provided code:" + Environment.NewLine + string.Join(Environment.NewLine, commentFooter))),
            []);
    }

    /// <summary>
    /// Turns an ILSpy "// sequence point: (line, col) to (line, col)" comment into the actual C# source it
    /// maps to. Non-matching lines (i.e. the IL itself) are returned unchanged. The returned span gives the
    /// position/length of the C# snippet within the returned text (length 0 when there is no snippet).
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

    private Compilation Compile(string code, OptimizationLevel optimizationLevel, OutputKind outputKind)
    {
        var ast = CSharpSyntaxTree.ParseText(code, parseOptions);
        var compilation = CSharpCompilation.Create("CompilationForDisassembly",
            [ast],
            referenceService.LoadedReferenceAssemblies,
            compilationOptions
                .WithOutputKind(outputKind)
                .WithOptimizationLevel(optimizationLevel)
                .WithUsings(referenceService.Usings.Select(u => u.Name?.ToString()).WhereNotNull())
        );
        return compilation;
    }

    internal delegate Compilation CompileDelegate(string code, OptimizationLevel optimizationLevel);

    [GeneratedRegex(@"^(?<indent>\s*)// sequence point: \(line (?<sl>\d+), col (?<sc>\d+)\) to \(line (?<el>\d+), col (?<ec>\d+)\).*$")]
    private static partial Regex SequencePointRegex();
}
