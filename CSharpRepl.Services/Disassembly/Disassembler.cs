// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using CSharpRepl.Services.Roslyn.References;
using CSharpRepl.Services.Roslyn.Scripting;
using ICSharpCode.Decompiler;
using ICSharpCode.Decompiler.Disassembler;
using ICSharpCode.Decompiler.Metadata;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Threading;

namespace CSharpRepl.Services.Disassembly;

/// <summary>
/// Shows the IL code for the user's C# code.
/// </summary>
internal class Disassembler
{
    private readonly AssemblyReferenceService referenceService;
    private readonly (string name, CompileDelegate compile)[] compilers;
    private readonly CSharpCompilationOptions compilationOptions;

    public Disassembler(CSharpCompilationOptions compilationOptions, AssemblyReferenceService referenceService, ScriptRunner scriptRunner)
    {
        this.compilationOptions = compilationOptions;
        this.referenceService = referenceService;

        // we will try to compile the user's code several different ways. The first one that succeeds will be used.
        this.compilers = new (string name, CompileDelegate compile)[]
        {
            // "console application" will work for standalone statements, due to C#'s top-level statement feature.
            (name: "Console Application (with top-level statements)",
             compile: (code, optimizationLevel) => Compile(code, optimizationLevel, OutputKind.ConsoleApplication)),
            // "DLL" will work if the user doesn't have statements, for example, they're only defining types.
            (name: "DLL",
             compile: (code, optimizationLevel) => Compile(code, optimizationLevel, OutputKind.DynamicallyLinkedLibrary)),
            // Compiling as a script will work for most other cases, but it's quite verbose so we use it as a last resort.
            (name: "Scripting session (will be overly verbose)",
             compile: (code, optimizationLevel) => scriptRunner.CompileTransient(code, optimizationLevel))
        };
    }

    public EvaluationResult Disassemble(string code, bool debugMode)
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

        foreach (var (name, compile) in compilers)
        {
            // step 1, try to compile the input using one of our compiler configurations.
            stream.SetLength(0);
            var compiled = compile(code, optimizationLevel);
            var compilationResult = compiled.Emit(stream);

            // step 2, disassemble the compiled result and return the IL
            if (compilationResult.Success)
            {
                commentFooter.Add($"// Compiling code as {name}: succeeded.");
                stream.Position = 0;
                var file = new PEFile(Guid.NewGuid().ToString(), stream, PEStreamOptions.LeaveOpen);

                var ilCodeOutput = DisassembleFile(file);

                var ilCode =
                    string.Join('\n', ilCodeOutput
                        .ToString()
                        .Split(new[] { "\r\n", "\n" }, StringSplitOptions.None)
                        .Select(line => line.TrimEnd()) // output has trailing spaces on some lines, clean those up
                    )
                    + string.Join('\n', commentFooter);
                return new EvaluationResult.Success(code, ilCode, Array.Empty<MetadataReference>());
            }

            // failure, we couldn't compile it, move on to the next compiler configuration.
            commentFooter.Add($"// Compiling code as {name}: failed.");
            commentFooter.AddRange(compilationResult
                .Diagnostics
                .Where(d => d.Severity == DiagnosticSeverity.Error)
                .Select(err => "//   - " + err.GetMessage())
            );
        }

        return new EvaluationResult.Error(
            new InvalidOperationException("// Could not compile provided code:" + Environment.NewLine + string.Join(Environment.NewLine, commentFooter))
        );
    }

    private static PlainTextOutput DisassembleFile(PEFile file)
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
        if (definedTypeNames.Except(new[] { "<Module>", "Program" }).Any())
        {
            new ReflectionDisassembler(ilCodeOutput, CancellationToken.None).WriteModuleContents(file); // writes to the "ilCodeOutput" variable
            return ilCodeOutput;
        }

        // if we find any additional methods, fallback to disassembling and returning the entire file.
        var programType = asmReader.GetTypeDefinition(definedTypes[Array.IndexOf(definedTypeNames, "Program")]);
        var methods = programType.GetMethods().ToArray();
        var methodNames = methods.Select(m => asmReader.GetString(asmReader.GetMethodDefinition(m).Name)).ToArray();
        if (methodNames.Except(new[] { "<Main>$", ".ctor" }).Any())
        {
            new ReflectionDisassembler(ilCodeOutput, CancellationToken.None).WriteModuleContents(file); // writes to the "ilCodeOutput" variable
            return ilCodeOutput;
        }

        // we successfully found that there's only a simple Program.Main, so disassemble just the Main method body.
        new MethodBodyDisassembler(ilCodeOutput, CancellationToken.None).Disassemble(file, methods[Array.IndexOf(methodNames, "<Main>$")]);
        return ilCodeOutput;
    }

    private Compilation Compile(string code, OptimizationLevel optimizationLevel, OutputKind outputKind)
    {
        var ast = CSharpSyntaxTree.ParseText(code, new CSharpParseOptions(LanguageVersion.Latest));
        var compilation = CSharpCompilation.Create("CompilationForDisassembly",
            new[] { ast },
            referenceService.LoadedReferenceAssemblies,
            compilationOptions
                .WithOutputKind(outputKind)
                .WithOptimizationLevel(optimizationLevel)
                .WithUsings(referenceService.Usings.Select(u => u.Name.ToString()))
        );
        return compilation;
    }

    internal delegate Compilation CompileDelegate(string code, OptimizationLevel optimizationLevel);
}
