// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Reflection.Metadata;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace CSharpRepl.Services.SymbolExploration;

/// <summary>
/// Looks up SourceLink JSON metadata in a PDB for a given Sequence Point to get
/// the URL of the document, e.g. on GitHub.
/// </summary>
/// <remarks>
/// The following archived code was used as reference for how to interact with sourcelink json documents:
/// https://github.com/ctaggart/SourceLink/blob/master/dotnet-sourcelink/Program.cs
/// See also
/// https://github.com/dotnet/runtime/blob/main/docs/design/specs/PortablePdb-Metadata.md#sequence-points-blob
/// </remarks>
internal sealed class SourceLinkLookup
{
    private static readonly Guid SourceLinkId = new("CC110556-A091-4D38-9FEC-25AB9A351A6A");
    private readonly GitHubSourceLinkHost[] sourceLinkHosts;

    public SourceLinkLookup()
    {
        // if a host is not in this list, we pass back the link unmodified.
        this.sourceLinkHosts = new[] { new GitHubSourceLinkHost() };
    }

    public bool TryGetSourceLinkUrl(MetadataReader symbolReader, SequencePointRange sequencePointRange, out string? url)
    {
        var sourceLinkMetadata = FindSourceLinkMetadata(symbolReader);

        var sequencePointDocumentName = symbolReader.GetString(
            symbolReader.GetDocument(sequencePointRange.Start.Document).Name
        );

        url = GetUrl(sourceLinkMetadata, sequencePointDocumentName);

        if (url is null) return false;

        // optionally rewrite the url to something more friendly, based on the host.
        foreach (var host in sourceLinkHosts)
        {
            if (host.Rewrite(url, sequencePointRange) is string rewrittenUrl)
            {
                url = rewrittenUrl;
                break;
            }
        }
        return true;
    }

    /// <summary>
    /// Returns the source link json for the provided PDB metadata reader
    /// </summary>
    private static SourceLinkJson? FindSourceLinkMetadata(MetadataReader symbolReader)
    {
        // https://github.com/ctaggart/SourceLink/blob/master/dotnet-sourcelink/Program.cs
        var blobh = default(BlobHandle);
        foreach (var cdih in symbolReader.GetCustomDebugInformation(EntityHandle.ModuleDefinition))
        {
            var cdi = symbolReader.GetCustomDebugInformation(cdih);
            if (symbolReader.GetGuid(cdi.Kind) == SourceLinkId)
                blobh = cdi.Value;
        }

        var utf8JsonBytes = symbolReader.GetBlobBytes(blobh);
        var json = JsonSerializer.Deserialize<SourceLinkJson>(utf8JsonBytes);
        return json;
    }

    /// <summary>
    /// Retrieves the SourceLink URL for the provided Sequence Points Blob document.
    /// </summary>
    private static string? GetUrl(SourceLinkJson? json, string? file)
    {
        if (json is null || file is null) return null;

        foreach (var key in json.Documents.Keys)
        {
            if (key.Contains('*'))
            {
                var pattern = Regex.Escape(key).Replace(@"\*", "(.+)");
                var regex = new Regex(pattern);
                var m = regex.Match(file);
                if (!m.Success) continue;
                var url = json.Documents[key];
                var path = m.Groups[1].Value.Replace(@"\", "/");
                return url.Replace("*", path);
            }
            else
            {
                if (!key.Equals(file, StringComparison.Ordinal)) continue;
                return json.Documents[key];
            }
        }
        return null;
    }

    private record SourceLinkJson(
        [property: JsonPropertyName("documents")] IDictionary<string, string> Documents
    );
}
