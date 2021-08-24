using System;
using System.Collections.Generic;
using System.Reflection.Metadata;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace CSharpRepl.Services.SymbolExploration
{
    /// <summary>
    /// Looks up SourceLink metadata in a PDB for a given Sequence Point to get the corresponding URL of the document, e.g. on GitHub.
    /// </summary>
    /// <remarks>https://github.com/dotnet/runtime/blob/main/docs/design/specs/PortablePdb-Metadata.md#sequence-points-blob</remarks>
    sealed class SourceLinkLookup
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

            var documentName = symbolReader.GetString(
                symbolReader.GetDocument(sequencePointRange.Start.Document).Name
            );

            url = GetUrl(sourceLinkMetadata, documentName);

            if (url is null) return false;

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

        private static string? GetUrl(SourceLinkJson? json, string? file)
        {
            if (json is null || file is null) return null;

            foreach (var key in json.documents.Keys)
            {
                if (key.Contains('*'))
                {
                    var pattern = Regex.Escape(key).Replace(@"\*", "(.+)");
                    var regex = new Regex(pattern);
                    var m = regex.Match(file);
                    if (!m.Success) continue;
                    var url = json.documents[key];
                    var path = m.Groups[1].Value.Replace(@"\", "/");
                    return url.Replace("*", path);
                }
                else
                {
                    if (!key.Equals(file, StringComparison.Ordinal)) continue;
                    return json.documents[key];
                }
            }
            return null;
        }

        private record SourceLinkJson(IDictionary<string, string> documents);
    }
}
