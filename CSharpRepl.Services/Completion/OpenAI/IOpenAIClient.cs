#region License Header
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.
#endregion

using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace CSharpRepl.Services.Completion.OpenAI;

internal interface IOpenAIClient
{
    Task<HttpResponseMessage> IssueRequestAsync(IReadOnlyList<string> submissions, string code, int caret, CancellationToken cancellationToken);

    string? ParseLineToCompletion(string line);

    bool EmitLeadingNewline => false;
}
