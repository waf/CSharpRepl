using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using PrettyPrompt;
using PrettyPrompt.Highlighting;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace CSharpLangRepl
{
    class Program
    {
        private static ScriptEvaluator evaluator;

        static async Task Main(string[] args)
        {
            var prompt = new Prompt(completionHandler: complete, highlightHandler: highlight, forceSoftEnterHandler: ShouldBeSoftEnter);
            evaluator = new ScriptEvaluator();

            while (true)
            {
                var response = await prompt.ReadLineAsync("> ");
                if (response.Success)
                {
                    if (response.Text == "exit") break;

                    var result = await evaluator.Evaluate(new TextInput(response.Text));
                    if(result is EvaluationResult.Error err)
                    {
                        Console.Error.WriteLine(err.Exception.Message);
                    }
                    else if (result is EvaluationResult.Success ok)
                    {
                        Console.WriteLine(ok.ReturnValue);
                    }
                }
            }
        }

        private static Task<bool> ShouldBeSoftEnter(string text)
        {
            var parseOptions = new CSharpParseOptions(LanguageVersion.Latest, kind: SourceCodeKind.Script);
            var syntaxTree = CSharpSyntaxTree.ParseText(text, parseOptions);
            return Task.FromResult(
                !SyntaxFactory.IsCompleteSubmission(syntaxTree)
            );
        }

        private static async Task<IReadOnlyCollection<FormatSpan>> highlight(string text)
        {
            var results = await evaluator.Highlight(text);

            return results
                .Select(r => new FormatSpan(r.TextSpan.Start, r.TextSpan.Length, ToColor(r.ClassificationType)))
                .Where(f => f.Formatting is not null)
                .ToArray();
        }

        private static ConsoleFormat ToColor(string classificationType) =>
            classificationType switch
            {
                "string" => new ConsoleFormat(AnsiColor.BrightYellow),
                "number" => new ConsoleFormat(AnsiColor.BrightBlue),
                "operator" => new ConsoleFormat(AnsiColor.Magenta),
                "keyword" => new ConsoleFormat(AnsiColor.Magenta),
                "keyword - control" => new ConsoleFormat(AnsiColor.Magenta),

                "record class name" => new ConsoleFormat(AnsiColor.BrightCyan),
                "class name" => new ConsoleFormat(AnsiColor.BrightCyan),
                "struct name" => new ConsoleFormat(AnsiColor.BrightCyan),

                "comment" => new ConsoleFormat(AnsiColor.Cyan),
                _ => null
            };

        private static async Task<IReadOnlyList<PrettyPrompt.Completion.CompletionItem>> complete(string text, int caret)
        {
            var results = await evaluator.Complete(text, caret);
            return results?.Items
                .OrderByDescending(i => i.Rules.MatchPriority)
                .Select(r => new PrettyPrompt.Completion.CompletionItem
                {
                    StartIndex = r.Span.Start,
                    ReplacementText = r.DisplayText
                })
                .ToArray()
                ??
                Array.Empty<PrettyPrompt.Completion.CompletionItem>();
        }
    }
}

            /*
            var host = MefHostServices.Create(MefHostServices.DefaultAssemblies);
            var solution = await Compile(input, host);
            var projectId = solution.ProjectIds.Single();

            var followUp = Console.ReadLine();

            var documentId1 = DocumentId.CreateNewId(projectId);
            var documentInfo1 = DocumentInfo.Create(
                id: documentId1,
                name: "Document" + _submissionIndex + ".cs",
                sourceCodeKind: SourceCodeKind.Script,
                loader: TextLoader.From(TextAndVersion.Create(SourceText.From(input), VersionStamp.Create())));
            solution = solution.AddDocument(documentInfo1);

            var documentId2 = DocumentId.CreateNewId(projectId);
            var documentInfo2 = DocumentInfo.Create(
                id: documentId2,
                name: "FollowUp" + _submissionIndex + ".cs",
                sourceCodeKind: SourceCodeKind.Script,
                loader: TextLoader.From(TextAndVersion.Create(SourceText.From(followUp), VersionStamp.Create())));
            solution = solution.AddDocument(documentInfo2);
            var document2 = solution.GetDocument(documentId2);
            var document3 = document2.WithText(SourceText.From("Console."));
            var result = document3.GetTextAsync().Result.WithChanges(new TextChange(new TextSpan(0, 2), "CO"));

            var service = CompletionService.GetService(document3);
            var completions = await service.GetCompletionsAsync(document3, "Console.".Length);
            ;
        }

        static async Task<Solution> Compile(string input, MefHostServices host)
        {
            Assembly assembly = null;
            var tree = CSharpSyntaxTree
                .ParseText(
                    input,
                    CSharpParseOptions.Default.WithKind(SourceCodeKind.Script).WithLanguageVersion(LanguageVersion.Preview)
                );

            var scriptCompilation = CSharpCompilation.CreateScriptCompilation(
                Path.GetRandomFileName(),
                tree,
                references,
                compilationOptions,
                _previousCompilation
            );

            var errorDiagnostics = scriptCompilation.GetDiagnostics().Where(x => x.Severity == DiagnosticSeverity.Error).ToList();
            if (errorDiagnostics.Any())
            {
                Console.WriteLine(errorDiagnostics.First().GetMessage());
            }
            using (var peStream = new MemoryStream())
            {
                var emitResult = scriptCompilation.Emit(peStream);
                if (emitResult.Success)
                {
                    _submissionIndex++;
                    _previousCompilation = scriptCompilation;
                    var bytes = peStream.ToArray();
                    ConstructedReferences.Add(MetadataReference.CreateFromImage(bytes));
                    assembly = Assembly.Load(bytes);
                    Assemblies.Add(assembly);
                }
                else
                {
                    Console.WriteLine("Emit failed");
                }
            }

            var previousOut = Console.Out;
            var writer = new StringWriter();
            Console.SetOut(writer);

            var entryPoint = _previousCompilation.GetEntryPoint(CancellationToken.None);
            var type = assembly.GetType($"{entryPoint.ContainingNamespace.MetadataName}.{entryPoint.ContainingType.MetadataName}");
            var entryPointMethod = type.GetMethod(entryPoint.MetadataName);

            var submission = (Func<object[], Task>)entryPointMethod.CreateDelegate(typeof(Func<object[], Task>));
            if (_submissionIndex >= _submissionStates.Length)
            {
                Array.Resize(ref _submissionStates, Math.Max(_submissionIndex, _submissionStates.Length * 2));
            }

            var returnValue = await (Task<object>)submission(_submissionStates);
            Console.SetOut(previousOut);

            if (returnValue != null)
            {
                Console.WriteLine(CSharpObjectFormatter.Instance.FormatObject(returnValue));
            }

            var projectInfo = ProjectInfo
                .Create(
                    id: ProjectId.CreateNewId(),
                    version: VersionStamp.Create(),
                    name: "Project" + _submissionIndex,
                    assemblyName: "Project" + _submissionIndex,
                    language: LanguageNames.CSharp
                    //isSubmission: true
                )
                .WithMetadataReferences(references.Concat(ConstructedReferences))
                .WithCompilationOptions(compilationOptions);
            return new AdhocWorkspace(host)
                .AddProject(projectInfo)
                .Solution;
        }

    }
}
            */
