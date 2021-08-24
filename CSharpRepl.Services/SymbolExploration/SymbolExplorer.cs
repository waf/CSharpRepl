// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Microsoft.CodeAnalysis;
using Document = Microsoft.CodeAnalysis.Document;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.SymbolStore;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Reflection.Metadata;
using CSharpRepl.Services.Roslyn.References;
using System.Reflection;
using CSharpRepl.Services.Roslyn.Scripting;
using CSharpRepl.Services.Extensions;

namespace CSharpRepl.Services.SymbolExploration
{
    /// <summary>
    /// Provides information (e.g. types) of symbols in a <see cref="Document"/>.
    /// </summary>
    internal sealed class SymbolExplorer
    {
        private readonly ScriptRunner scriptRunner;
        private readonly SourceLinkLookup sourceLinkLookup;
        private readonly AssemblyReferenceService referenceService;
        private readonly SymbolDisplayFormat displayOptions;

        public SymbolExplorer(AssemblyReferenceService referenceService, ScriptRunner scriptRunner)
        {
            this.scriptRunner = scriptRunner;
            this.sourceLinkLookup = new SourceLinkLookup();
            this.referenceService = referenceService;
             this.displayOptions = new SymbolDisplayFormat(
                 SymbolDisplayGlobalNamespaceStyle.Omitted,
                 SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces,
                 SymbolDisplayGenericsOptions.None,
                 SymbolDisplayMemberOptions.IncludeContainingType,
                 SymbolDisplayDelegateStyle.NameOnly,
                 SymbolDisplayExtensionMethodStyle.StaticMethod,
                 SymbolDisplayParameterOptions.None,
                 SymbolDisplayPropertyStyle.NameOnly,
                 SymbolDisplayLocalOptions.None,
                 SymbolDisplayKindOptions.None,
                 SymbolDisplayMiscellaneousOptions.ExpandNullable
            );
        }
        
        private ISymbol? GetSymbolAtPosition(string text, int position)
        {
            var compilation = scriptRunner.CompileTransient(text, OptimizationLevel.Debug);
            var tree = compilation.SyntaxTrees.Single();
            var semanticModel = compilation.GetSemanticModel(tree);

            if (semanticModel is null) return null;

            // the most obvious way to implement this would be using GetEnclosingSymbol or ChildThatContainsPosition.
            // however, neither of those appears to work for script-type projects. GetEnclosingSymbol always returns "<Initialize>".
            var symbols =
                from node in semanticModel.SyntaxTree.GetRoot().DescendantNodes()
                where node.Span.Start < position && position < node.Span.End
                orderby node.Span.Length
                let symbolInfo = semanticModel.GetSymbolInfo(node)
                select new { node, symbol = symbolInfo.Symbol ?? symbolInfo.CandidateSymbols.FirstOrDefault() };

            var mostSpecificSymbol = symbols.FirstOrDefault(s => s.symbol is not null);

            return mostSpecificSymbol?.symbol;
        }

        public async Task<SymbolResult> LookupSymbolAtPosition(string text, int position)
        {
            var symbolAtPosition = GetSymbolAtPosition(text, position);
            if (symbolAtPosition is null) return SymbolResult.Unknown;

            var retValue = new SymbolResult(
                SymbolDisplay: symbolAtPosition.ToDisplayString(displayOptions),
                Url: ""
            );

            var assembly = symbolAtPosition.ContainingAssembly;
            var assemblyReader = assembly?.GetMetadata()?.GetModules().FirstOrDefault()?.GetMetadataReader();
            var assemblyFilePath = referenceService
                .LoadedImplementationAssemblies
                .LastOrDefault(a =>
                    !string.IsNullOrEmpty(a.Display)
                    && Path.GetFileNameWithoutExtension(a.Display) == assembly?.Identity.Name 
                    && AssemblyName.GetAssemblyName(a.Display).ToString() == assembly?.Identity.ToString())
                ?.Display;

            if(assemblyReader is null || assemblyFilePath is null)
            {
                return retValue;
            }

            TypeDefinition type = assemblyReader.TypeDefinitions
                .Select(t => assemblyReader.GetTypeDefinition(t))
                .FirstOrDefault(t =>
                    assemblyReader.GetString(t.Namespace) == symbolAtPosition.ContainingNamespace.ToDisplayString()
                    && assemblyReader.GetString(t.Name) == (symbolAtPosition.ContainingType?.Name ?? symbolAtPosition.Name)
                );

            using var debugSymbolLoader = new DebugSymbolLoader(assemblyFilePath);

            foreach (var key in debugSymbolLoader.GetSymbolFileNames())
            {
                using SymbolStoreFile symbolFile = await debugSymbolLoader.DownloadSymbolFile(key, CancellationToken.None);

                var symbolReader = debugSymbolLoader.ReadAsPortablePdb(symbolFile);
                if (symbolReader is null)
                {
                    continue;
                }

                // find the Sequence Points in the PDB that will give us document (e.g. filepath) and line numbers for the given method/property.
                var sequencePointRange = FindSequencePointRangeForSymbol(symbolReader, assemblyReader, type, symbolAtPosition);

                if (sequencePointRange is null) continue;

                // use source link the transform filepath to repository  url (e.g. Github).
                if (sourceLinkLookup.TryGetSourceLinkUrl(symbolReader, sequencePointRange.Value, out var url))
                {
                    return retValue with { Url = url };
                }
            }

            return retValue;
        }

        private static SequencePointRange? FindSequencePointRangeForSymbol(MetadataReader symbolReader, MetadataReader assemblyReader, TypeDefinition type, ISymbol symbolAtPosition) =>
            symbolAtPosition switch
            {
                IMethodSymbol method => FindMethod(symbolReader, assemblyReader, type, method),
                IPropertySymbol prop => FindProperty(symbolReader, assemblyReader, type, prop),
                _ => null
            } ?? FallbackFirstSequencePoint(symbolReader, type);

        private static SequencePointRange? FindMethod(MetadataReader symbolReader, MetadataReader assemblyReader, TypeDefinition type, IMethodSymbol method)
        {
            var methodHandle = type.GetMethods()
                .Select(m => new { handle = m, method = assemblyReader.GetMethodDefinition(m).Name })
                .FirstOrDefault(m => assemblyReader.GetString(m.method) == method.Name)
                ?.handle;

            if (methodHandle is null) return null;

            var sp = symbolReader.GetMethodDebugInformation(methodHandle.Value).GetSequencePoints();
            return sp.Any() ? new SequencePointRange(sp.First(), sp.Last()) : null;
        }

        private static SequencePointRange? FindProperty(MetadataReader symbolReader, MetadataReader assemblyReader, TypeDefinition type, IPropertySymbol method)
        {
            var methodHandle = type.GetProperties()
                .Select(m => new { handle = m, method = assemblyReader.GetPropertyDefinition(m) })
                .FirstOrDefault(m => assemblyReader.GetString(m.method.Name) == method.Name);

            if (methodHandle is null) return null;

            var accessors = methodHandle.method.GetAccessors();
            var getterOrSetter =
                !accessors.Getter.IsNil ? accessors.Getter
                : !accessors.Setter.IsNil ? accessors.Setter
                : accessors.Others.First();

            var sp = symbolReader.GetMethodDebugInformation(getterOrSetter).GetSequencePoints();
            return sp.Any() ? new SequencePointRange(sp.First(), sp.Last()) : null;
        }

        private static SequencePointRange? FallbackFirstSequencePoint(MetadataReader symbolReader, TypeDefinition type)
        {
            var sp = type.GetMethods().SelectMany(method => symbolReader.GetMethodDebugInformation(method).GetSequencePoints().Where(s => !s.IsHidden));
            return sp.Any() ? new SequencePointRange(sp.First(), null) : null;
        }

    }

    public readonly struct SequencePointRange
    {
        public SequencePointRange(SequencePoint start, SequencePoint? end)
        {
            Start = start;
            End = end;
        }

        public SequencePoint Start { get; }
        public SequencePoint? End { get; }
    }

    public record SymbolResult(string? SymbolDisplay, string? Url)
    {
        public static readonly SymbolResult Unknown = new SymbolResult("Unknown", "Unknown");
    }
}
