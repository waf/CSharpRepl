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

    public void CheckMaxTokensAndThrow(int maxTokens)
    {
    }
}
