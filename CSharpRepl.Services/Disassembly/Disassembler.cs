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
using System.Reflection.PortableExecutable;
using System.Threading;

namespace CSharpRepl.Services.Disassembly
{
    /// <summary>
    /// Shows the IL code for the user's C# code.
    /// </summary>
    class Disassembler
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
                (name:"DLL",
                 compile: (code, optimizationLevel) => Compile(code, optimizationLevel, OutputKind.DynamicallyLinkedLibrary)),
                // Compiling as a script will work for most other cases, but it's quite verbose so we use it as a last resort.
                (name:"Scripting session (will be overly verbose)",
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
                    ? " Press Ctrl+F11 to disassemble in Release Mode."
                    : string.Empty)
            };

            // the disassembler will write to the ilCodeOutput variable when invoked.
            var ilCodeOutput = new PlainTextOutput { IndentationString = new string(' ', 4) };
            var disassembler = new ReflectionDisassembler(ilCodeOutput, CancellationToken.None);

            using var stream = new MemoryStream();

            foreach (var compiler in compilers)
            {
                stream.SetLength(0);
                var compiled = compiler.compile(code, optimizationLevel);
                var compilationResult = compiled.Emit(stream);
                if(compilationResult.Success)
                {
                    commentFooter.Add($"// Compiling code as {compiler.name}: succeeded.");
                    stream.Position = 0;
                    var file = new PEFile(Guid.NewGuid().ToString(), stream, PEStreamOptions.LeaveOpen);
                    disassembler.WriteModuleContents(file); // writes to the "ilCodeOutput" variable
                    var ilCode = 
                        string.Join('\n', ilCodeOutput
                            .ToString()
                            .Split(new[] { "\r\n", "\n" }, StringSplitOptions.None)
                            .Select(line => line.TrimEnd()) // output has trailing spaces on some lines, clean those up
                        )
                        + string.Join('\n', commentFooter);
                    return new EvaluationResult.Success(code, ilCode, Array.Empty<MetadataReference>());
                }
                else
                {
                    commentFooter.Add($"// Compiling code as {compiler.name}: failed.");
                    commentFooter.AddRange(compilationResult
                        .Diagnostics
                        .Where(d => d.Severity == DiagnosticSeverity.Error)
                        .Select(err => "//   - " + err.GetMessage())
                    );
                }
            }

            return new EvaluationResult.Error(
                new InvalidOperationException("// Could not compile provided code:" + Environment.NewLine + string.Join(Environment.NewLine, commentFooter))
            );
        }

        private Compilation Compile(string code, OptimizationLevel optimizationLevel, OutputKind outputKind)
        {
            var ast = CSharpSyntaxTree.ParseText(code, new CSharpParseOptions(LanguageVersion.Latest));
            var compilation = CSharpCompilation.Create("CompilationForDecompilation",
                new[] { ast },
                referenceService.LoadedReferenceAssemblies,
                compilationOptions
                    .WithOutputKind(outputKind)
                    .WithOptimizationLevel(optimizationLevel)
                    .WithUsings(referenceService.Usings.Select(u => u.Name.ToString()))
            );
            return compilation;
        }

        delegate Compilation CompileDelegate(string code, OptimizationLevel optimizationLevel);
    }
}
