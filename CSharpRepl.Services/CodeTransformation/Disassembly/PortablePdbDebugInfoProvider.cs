// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection.Metadata;
using ICSharpCode.Decompiler.DebugInfo;
using DecompilerSequencePoint = ICSharpCode.Decompiler.DebugInfo.SequencePoint;

namespace CSharpRepl.Services.CodeTransformation.Disassembly;

/// <summary>
/// Exposes the sequence points from an in-memory portable PDB to the ILSpy disassemblers,
/// so the IL output can be annotated with the C# source line each instruction came from.
/// </summary>
internal sealed class PortablePdbDebugInfoProvider : IDebugInfoProvider, IDisposable
{
    private readonly MetadataReaderProvider provider;
    private readonly MetadataReader reader;

    public PortablePdbDebugInfoProvider(Stream portablePdbStream)
    {
        // PrefetchMetadata reads the whole PDB into memory, so we don't depend on the
        // stream staying open after construction.
        provider = MetadataReaderProvider.FromPortablePdbStream(portablePdbStream, MetadataStreamOptions.PrefetchMetadata);
        reader = provider.GetMetadataReader();
    }

    public string Description => "In-memory portable PDB";

    // Not backed by a file on disk; the source lives in the REPL input.
    public string SourceFileName => string.Empty;

    public IList<DecompilerSequencePoint> GetSequencePoints(MethodDefinitionHandle method)
    {
        var debugHandle = method.ToDebugInformationHandle();
        if (debugHandle.IsNil)
        {
            return [];
        }

        var debugInfo = reader.GetMethodDebugInformation(debugHandle);
        var points = new List<DecompilerSequencePoint>();
        foreach (var sp in debugInfo.GetSequencePoints())
        {
            var documentUrl = sp.Document.IsNil
                ? string.Empty
                : reader.GetString(reader.GetDocument(sp.Document).Name);

            points.Add(new DecompilerSequencePoint
            {
                Offset = sp.Offset,
                StartLine = sp.StartLine,
                StartColumn = sp.StartColumn,
                EndLine = sp.EndLine,
                EndColumn = sp.EndColumn,
                DocumentUrl = documentUrl,
            });
        }

        // ILSpy expects each sequence point's EndOffset to mark where the next one begins.
        points.Sort((a, b) => a.Offset.CompareTo(b.Offset));
        for (int i = 0; i < points.Count - 1; i++)
        {
            points[i].EndOffset = points[i + 1].Offset;
        }
        if (points.Count > 0)
        {
            points[^1].EndOffset = points[^1].Offset;
        }
        return points;
    }

    public IList<Variable> GetVariables(MethodDefinitionHandle method) => [];

    public bool TryGetName(MethodDefinitionHandle method, int index, out string name)
    {
        name = string.Empty;
        return false;
    }

    public bool TryGetExtraTypeInfo(MethodDefinitionHandle method, int index, out PdbExtraTypeInfo extraTypeInfo)
    {
        extraTypeInfo = default;
        return false;
    }

    public void Dispose() => provider.Dispose();
}
