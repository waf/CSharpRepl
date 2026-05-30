// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.IO;
using System.Threading.Tasks;
using CSharpRepl.Services.Roslyn;
using Xunit;

namespace CSharpRepl.Tests;

/// <summary>
/// Drives <see cref="Program.Main"/> end-to-end in its non-interactive "piped input" mode (the branch
/// taken when stdin is redirected). Lives in the RoslynServices collection so its Roslyn initialization
/// is serialized with the other heavy tests rather than contending with them in parallel.
/// </summary>
[Collection(nameof(RoslynServices))]
public class ProgramPipedInputTests
{
    [Fact]
    public async Task MainMethod_CollectedPipedInput_EvaluatesAndExitsSuccessfully()
    {
        // The interactive prompt path requires a real terminal; this test is only meaningful when
        // stdin is redirected (which it is under the test host / CI). Bail out safely otherwise so
        // we never block waiting for interactive input.
        if (!Console.IsInputRedirected) return;

        var originalIn = Console.In;
        using var outputCollector = OutputCollector.Capture(out _);
        try
        {
            Console.SetIn(new StringReader("1 + 1" + Environment.NewLine));

            var exitCode = await Program.Main([]);

            Assert.Equal(ExitCodes.Success, exitCode);
        }
        finally
        {
            Console.SetIn(originalIn);
        }
    }

    [Fact]
    public async Task MainMethod_StreamPipedInputWithoutRedirectedInput_ReturnsParseError()
    {
        // --streamPipedInput only makes sense with redirected stdin. When stdin is NOT redirected the
        // program reports a configuration error and exits. (When the test host redirects stdin this
        // instead streams the piped input, which is exercised by the collected-input test above.)
        if (Console.IsInputRedirected) return;

        using var outputCollector = OutputCollector.Capture(out _, out var capturedError);

        var exitCode = await Program.Main(["--streamPipedInput"]);

        Assert.Equal(ExitCodes.ErrorParseArguments, exitCode);
        Assert.Contains("streamPipedInput", capturedError.ToString());
    }
}
