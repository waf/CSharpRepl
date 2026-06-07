// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CSharpRepl.Services.CodeTransformation.Lowering;
using CSharpRepl.Services.CodeTransformation.Disassembly;
using CSharpRepl.Services.Extensions;
using CSharpRepl.Services.Roslyn.References;
using CSharpRepl.Services.Roslyn.Scripting;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;
using Microsoft.CodeAnalysis.Text;

namespace CSharpRepl.Services.CodeTransformation;

/// <summary>
/// Entry point for the REPL's IL disassembly and "lowered" C# decompilation. Both share the same compile step (here).
/// </summary>
internal sealed class CodeTransformer
{
    private readonly AssemblyReferenceService referenceService;
    private readonly (string name, CompileDelegate compile)[] compilers;
    private readonly CSharpParseOptions parseOptions;
    private readonly CSharpCompilationOptions compilationOptions;
    private readonly Disassembler disassembler = new();
    private readonly CodeLowerer codeLowerer;

    public CodeTransformer(CSharpParseOptions parseOptions, CSharpCompilationOptions compilationOptions, AssemblyReferenceService referenceService, ScriptRunner scriptRunner)
    {
        this.parseOptions = parseOptions.WithKind(SourceCodeKind.Regular);
        this.compilationOptions = compilationOptions;
        this.referenceService = referenceService;
        this.codeLowerer = new CodeLowerer(referenceService);

        // we will try to compile the user's code several different ways. The first one that succeeds will be used.
        this.compilers =
        [
            // "console application" will work for standalone statements, due to C#'s top-level statement feature.
            (name: "Console Application (with top-level statements)",
             compile: (code, optimizationLevel) => CreateCompilation(code, optimizationLevel, OutputKind.ConsoleApplication)),
            // "DLL" will work if the user doesn't have statements, for example, they're only defining types.
            (name: "DLL",
             compile: (code, optimizationLevel) => CreateCompilation(code, optimizationLevel, OutputKind.DynamicallyLinkedLibrary)),
            // Compiling as a script will work for most other cases, but it's quite verbose so we use it as a last resort.
            (name: "Scripting session (will be overly verbose)",
             compile: scriptRunner.CompileTransient)
        ];
    }

    /// <summary>
    /// Compiles <paramref name="code"/> and renders the result as IL (interleaved with the original C# source in comments).
    /// </summary>
    public (EvaluationResult Result, IReadOnlyList<TextSpan> CommentSpans) Disassemble(string code, bool debugMode)
    {
        var optimizationLevel = debugMode ? OptimizationLevel.Debug : OptimizationLevel.Release;
        var modeHeader = $"// Disassembled in {optimizationLevel} Mode."
            + (optimizationLevel == OptimizationLevel.Debug ? " Press Ctrl+F9 to disassemble in Release Mode." : string.Empty);

        // a portable PDB is emitted so the IL can be annotated with the C# source line each instruction came from.
        using var compilation = Compile(code, optimizationLevel, modeHeader, emitPdb: true);
        return compilation.Error is not null
            ? (compilation.Error, [])
            : disassembler.Render(compilation);
    }

    /// <summary>
    /// Compiles <paramref name="code"/> and renders the result as "lowered" C# (state machines, closures, expanded syntax sugar, etc).
    /// </summary>
    public EvaluationResult Lower(string code, bool debugMode)
    {
        var optimizationLevel = debugMode ? OptimizationLevel.Debug : OptimizationLevel.Release;
        var modeHeader = $"// Lowered in {optimizationLevel} Mode."
            + (optimizationLevel == OptimizationLevel.Debug ? " Press Ctrl+F8 to lower in Release Mode." : string.Empty);

        using var compilation = Compile(code, optimizationLevel, modeHeader, emitPdb: false);
        return compilation.Error is not null
            ? compilation.Error
            : codeLowerer.Render(compilation);
    }

    /// <summary>
    /// Prepends the active usings to <paramref name="code"/>, then tries each compiler configuration and emits the
    /// first that succeeds to an in-memory assembly. On failure it is an <see cref="EvaluationResult.Error"/>.
    /// The result owns the streams and must be disposed.
    /// </summary>
    private CompilationResult Compile(string code, OptimizationLevel optimizationLevel, string modeHeaderComment, bool emitPdb)
    {
        var usings = referenceService.Usings.Select(u => u.NormalizeWhitespace().ToString());
        code = string.Join(Environment.NewLine, usings) + Environment.NewLine + code;

        var footer = new List<string> { modeHeaderComment };

        var stream = new MemoryStream();
        var pdbStream = emitPdb ? new MemoryStream() : null;
        var emitOptions = emitPdb ? new EmitOptions(debugInformationFormat: DebugInformationFormat.PortablePdb) : null;

        foreach (var (name, compile) in compilers)
        {
            stream.SetLength(0);
            pdbStream?.SetLength(0);
            var compiled = compile(code, optimizationLevel);
            var emitResult = compiled.Emit(stream, pdbStream: pdbStream, options: emitOptions);
            if (emitResult.Success)
            {
                footer.Add($"// Compiling code as {name}: succeeded.");
                stream.Position = 0;
                if (pdbStream is not null) pdbStream.Position = 0;
                return CompilationResult.Succeeded(code, stream, pdbStream, footer);
            }

            // failure, we couldn't compile it, move on to the next compiler configuration.
            footer.Add($"// Compiling code as {name}: failed.");
            footer.AddRange(emitResult
                .Diagnostics
                .Where(d => d.Severity == DiagnosticSeverity.Error)
                .Select(err => "//   - " + err.GetMessage()));
        }

        stream.Dispose();
        pdbStream?.Dispose();
        return CompilationResult.Failed(
            new EvaluationResult.Error(
                new InvalidOperationException("// Could not compile provided code:" + Environment.NewLine + string.Join(Environment.NewLine, footer))));
    }

    private Compilation CreateCompilation(string code, OptimizationLevel optimizationLevel, OutputKind outputKind)
    {
        var ast = CSharpSyntaxTree.ParseText(code, parseOptions);
        return CSharpCompilation.Create("CompilationForInspection",
            [ast],
            referenceService.LoadedReferenceAssemblies,
            compilationOptions
                .WithOutputKind(outputKind)
                .WithOptimizationLevel(optimizationLevel)
                .WithUsings(referenceService.Usings.Select(u => u.Name?.ToString()).WhereNotNull())
        );
    }

    private delegate Compilation CompileDelegate(string code, OptimizationLevel optimizationLevel);
}

/// <summary>
/// The outcome of <see cref="CodeTransformer"/>'s compile step: either a successfully emitted in-memory assembly
/// (with its optional PDB, the usings-prepended source, and a comment footer) or an
/// <see cref="EvaluationResult.Error"/>. Owns the emitted streams; dispose it once the assembly has been read.
/// </summary>
internal sealed class CompilationResult : IDisposable
{
    private readonly MemoryStream? assemblyStream;
    private readonly MemoryStream? pdbStream;

    private CompilationResult(string code, MemoryStream? assemblyStream, MemoryStream? pdbStream, IReadOnlyList<string> footer, EvaluationResult.Error? error)
    {
        Code = code;
        this.assemblyStream = assemblyStream;
        this.pdbStream = pdbStream;
        Footer = footer;
        Error = error;
    }

    /// <summary>The user's code with the active usings prepended (used as the result's Input and as source lines).</summary>
    public string Code { get; }

    /// <summary>Comment lines (mode header + per-configuration outcome) to append to the rendered output.</summary>
    public IReadOnlyList<string> Footer { get; }

    /// <summary>Non-null when compilation failed; the error message carries the full diagnostic footer.</summary>
    public EvaluationResult.Error? Error { get; }

    /// <summary>The emitted assembly. Only valid when <see cref="Error"/> is null.</summary>
    public MemoryStream AssemblyStream => assemblyStream ?? throw new InvalidOperationException("No assembly was emitted.");

    /// <summary>The emitted portable PDB, present only when one was requested. Only valid when <see cref="Error"/> is null.</summary>
    public MemoryStream? PdbStream => pdbStream;

    public static CompilationResult Succeeded(string code, MemoryStream assemblyStream, MemoryStream? pdbStream, IReadOnlyList<string> footer)
        => new(code, assemblyStream, pdbStream, footer, error: null);

    public static CompilationResult Failed(EvaluationResult.Error error)
        => new(code: string.Empty, assemblyStream: null, pdbStream: null, [], error);

    public void Dispose()
    {
        assemblyStream?.Dispose();
        pdbStream?.Dispose();
    }
}
