// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Microsoft.CodeAnalysis.Text;
using PrettyPrompt.Highlighting;

namespace Sharply.Services.SyntaxHighlighting
{
    public record HighlightedSpan(TextSpan TextSpan, AnsiColor Color);
}