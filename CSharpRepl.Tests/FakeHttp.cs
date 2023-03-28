#region License Header
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.
#endregion

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace CSharpRepl.Tests;

using RequestMatcher = Func<string, bool>;

internal class FakeHttp : HttpMessageHandler, IEnumerable<(RequestMatcher, (HttpStatusCode, string))>
{
    private List<(RequestMatcher requestMatcher, (HttpStatusCode status, string content) response)> mockResponses = new();

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var content = await request.Content.ReadAsStringAsync(cancellationToken);
        var response = mockResponses.First(r => r.requestMatcher(content)).response;
        return new HttpResponseMessage(response.status)
        {
            Content = new StringContent(response.content, Encoding.UTF8, "application/json")
        };
    }

    public void Add(RequestMatcher requestMatcher, HttpStatusCode statusCode, string content)
    {
        mockResponses.Add((requestMatcher, (statusCode, content)));
    }

    IEnumerator IEnumerable.GetEnumerator() =>
        ((IEnumerable)mockResponses).GetEnumerator();

    public IEnumerator<(RequestMatcher, (HttpStatusCode, string))> GetEnumerator() =>
        ((IEnumerable<(RequestMatcher, (HttpStatusCode, string))>)mockResponses).GetEnumerator();
}
