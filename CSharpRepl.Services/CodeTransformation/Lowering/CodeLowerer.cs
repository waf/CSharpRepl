// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.IO;
using System.Linq;
using System.Reflection.PortableExecutable;
using CSharpRepl.Services.Roslyn.References;
using CSharpRepl.Services.Roslyn.Scripting;
using ICSharpCode.Decompiler;
using ICSharpCode.Decompiler.CSharp;
using ICSharpCode.Decompiler.CSharp.OutputVisitor;
using ICSharpCode.Decompiler.CSharp.Syntax;
using ICSharpCode.Decompiler.Metadata;

namespace CSharpRepl.Services.CodeTransformation.Lowering;

/// <summary>
/// Renders a compiled assembly as "lowered" C#: it decompiles the assembly with ILSpy with high-level
/// reconstruction disabled, so compiler-generated constructs (async/await and iterator state machines,
/// lambda display classes, foreach expansions, etc.) are shown explicitly.
/// </summary>
internal sealed class CodeLowerer(AssemblyReferenceService referenceService)
{
    // four-space indentation, matching the rest of the REPL (avoids ILSpy's default tabs)
    private const string IndentationString = "    ";

    /// <param name="compilation">A successful compilation.</param>
    /// <returns>The lowered C# as an <see cref="EvaluationResult.Success"/>, or an <see cref="EvaluationResult.Error"/> if the assembly could not be lowered.</returns>
    public EvaluationResult Render(CompilationResult compilation)
    {
        // decompile the compiled result back to lowered C#.
        try
        {
            var csharp = DecompileToLoweredCSharp(compilation.AssemblyStream);
            var output = csharp.TrimEnd() + "\n\n" + string.Join('\n', compilation.Footer);

            // normalize to '\n'-only line endings: ILSpy emits Windows '\r\n', but PrettyPrompt's ANSI renderer advances lines with its own cursor-movement codes.
            return new EvaluationResult.Success(compilation.Code, output.Replace("\r\n", "\n"), []);
        }
        catch (Exception ex)
        {
            return new EvaluationResult.Error(
                new InvalidOperationException("// Compiled successfully, but could not decompile:" + Environment.NewLine + "//   - " + ex.Message));
        }
    }

    private string DecompileToLoweredCSharp(Stream assembly)
    {
        // PrefetchEntireImage reads the whole module up front so it stays valid for the duration of decompilation
        // and is decoupled from the stream the compiler reuses across compiler attempts.
        using var file = new PEFile(Guid.NewGuid().ToString(), assembly, PEStreamOptions.PrefetchEntireImage);
        using var resolver = new ImplementationAssemblyResolver(referenceService.ImplementationAssemblyPaths);
        var settings = CreateLoweringSettings();
        var decompiler = new CSharpDecompiler(file, resolver, settings);

        var syntaxTree = decompiler.DecompileWholeModuleAsSingleFile();

        // drop the lengthy assembly/module attribute boilerplate ILSpy emits at the top of a whole-module decompile.
        foreach (var section in syntaxTree.Members.OfType<AttributeSection>().ToList())
        {
            if (section.AttributeTarget is "assembly" or "module")
            {
                section.Remove();
            }
        }

        // Render the (now-edited) tree ourselves. We can't use DecompileWholeModuleAsString(): it re-decompiles from a fresh tree,
        // discarding the attribute removal above. Using token writer also lets us indent with four spaces.
        using var writer = new StringWriter();
        var tokenWriter = new TextWriterTokenWriter(writer) { IndentationString = IndentationString };
        syntaxTree.AcceptVisitor(new CSharpOutputVisitor(tokenWriter, settings.CSharpFormattingOptions));
        return writer.ToString();
    }

    /// <summary>
    /// Decompiler settings tuned for a "lowering" view: every high-level reconstruction pass that would rewrite
    /// compiler-generated code back into its original syntactic sugar is turned off, so the user sees what the
    /// compiler actually emitted.
    /// </summary>
    private static DecompilerSettings CreateLoweringSettings() => new DecompilerSettings(LanguageVersion.Latest)
    {
        // async/await and iterator state machines: show the generated structs and MoveNext methods.
        AsyncAwait = false,
        AsyncEnumerator = false,
        AsyncUsingAndForEachStatement = false,
        AwaitInCatchFinally = false,
        YieldReturn = false,

        // lambdas, anonymous methods, and local functions: show the generated display classes and methods.
        AnonymousMethods = false,
        UseLambdaSyntax = false,
        LocalFunctions = false,
        StaticLocalFunctions = false,
        AnonymousTypes = false,

        // statement-level sugar: expand into the underlying calls / control flow. (UsingDeclarations is left
        // enabled on purpose: disabling it fully-qualifies every type name, which is noise rather than lowering.)
        ForEachStatement = false,
        ForEachWithGetEnumeratorExtension = false,
        UsingStatement = false,
        LockStatement = false,
        SwitchStatementOnString = false,
        SwitchOnReadOnlySpanChar = false,
        SwitchExpressions = false,

        // expression-level sugar: show the underlying members and explicit forms.
        StringConcat = false,
        StringInterpolation = false,
        Utf8StringLiterals = false,
        QueryExpressions = false,
        ObjectOrCollectionInitializers = false,
        AutomaticProperties = false,
        GetterOnlyAutomaticProperties = false,
        AutomaticEvents = false,
        NamedArguments = false,
        OptionalArguments = false,
        TupleTypes = false,
        TupleConversions = false,
        TupleComparisons = false,
        Discards = false,
    };
}
