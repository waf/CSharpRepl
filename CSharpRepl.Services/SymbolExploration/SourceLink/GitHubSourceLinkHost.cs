using System.Linq;
using System.Reflection.Metadata;

namespace CSharpRepl.Services.SymbolExploration
{
    /// <summary>
    /// Rewrites the plaintext github URLs to the web UI URLs.
    /// raw.githubusercontent.com -> www.github.com
    /// </summary>
    sealed class GitHubSourceLinkHost
    {
        public string? Rewrite(string url, SequencePointRange sequencePointRange)
        {
            var parts = url.Split('/').ToList();
            if (parts.Count <= 5 || parts[2] != "raw.githubusercontent.com")
            {
                return null;
            }

            // generate a URL to the source code based on Sequence Point info
            SequencePoint lineStart = sequencePointRange.Start;
            SequencePoint? lineEnd = sequencePointRange.End;

            parts[2] = "www.github.com";
            parts.Insert(5, "blob");
            return string.Join("/", parts)
                + "#L" + lineStart.StartLine
                + (lineEnd.HasValue ? "-L" + lineEnd.Value.EndLine : "");
        }
    }
}