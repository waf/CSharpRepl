// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Linq;
using System.Reflection.Metadata;

namespace CSharpRepl.Services.SymbolExploration;

/// <summary>
/// Rewrites the plaintext github URLs to the web UI URLs.
/// i.e. raw.githubusercontent.com -> www.github.com
/// </summary>
internal sealed class GitHubSourceLinkHost
{
    public string? Rewrite(string url, SequencePointRange sequencePointRange)
    {
        var parts = url.Split('/').ToList();
        if (parts.Count <= 5 || parts[2] != "raw.githubusercontent.com")
        {
            return null;
        }

        // generate a URL to the source code based on Sequence Point info

        parts[2] = "www.github.com";
        parts.Insert(5, "blob");
        return string.Join("/", parts) + CreateLineNumberLocationHash(sequencePointRange);
    }

    private string CreateLineNumberLocationHash(SequencePointRange sequencePointRange)
    {
        SequencePoint lineStart = sequencePointRange.Start;
        SequencePoint? lineEnd = sequencePointRange.End;

        // design choice: if we can't highlight a range, don't highlight
        // anything. Otherwise, it's confusing for the user if we
        // highlight a random line in the source file.
        if (lineEnd is null) return string.Empty;

        return "#L" + lineStart.StartLine
            + (lineEnd is not null ? "-L" + lineEnd.Value.EndLine : "");
    }
}
