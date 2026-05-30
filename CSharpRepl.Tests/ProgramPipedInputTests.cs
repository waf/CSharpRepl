// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Threading.Tasks;
using CSharpRepl.Services.Roslyn;
using NSubstitute;
using Xunit;

namespace CSharpRepl.Tests;

/// <summary>
/// Drives <see cref="Program.RunAsync"/> in its non-interactive "piped input" mode. Input is supplied
/// through an injected fake console rather than the real standard input handle, so these tests are
/// deterministic and never block on stdin regardless of how the test host is invoked (interactive
/// terminal, CI with /dev/null, or a parent process that leaves stdin open). Lives in the RoslynServices
/// collection so its Roslyn initialization is serialized with the other heavy tests.
/// </summary>
[Collection(nameof(RoslynServices))]
public class ProgramPipedInputTests
{
    [Fact]
    public async Task RunAsync_CollectedPipedInput_EvaluatesAndExitsSuccessfully()
    {
        var (console, _) = FakeConsole.CreateStubbedOutput();
        console.ReadLine().Returns("1 + 1", (string)null); // one piped line, then end-of-input

        var exitCode = await Program.RunAsync([], console, inputRedirectedOverride: true);

        Assert.Equal(ExitCodes.Success, exitCode);
    }

    [Fact]
    public async Task RunAsync_StreamPipedInputWithoutRedirectedInput_ReturnsParseError()
    {
        // --streamPipedInput only makes sense with redirected stdin. When stdin is NOT redirected the
        // program reports a configuration error and exits.
        var (console, _, stderr) = FakeConsole.CreateStubbedOutputAndError();
        console.PrettyPromptConsole.IsErrorRedirected = true; // route WriteErrorLine to the captured error buffer

        var exitCode = await Program.RunAsync(["--streamPipedInput"], console, inputRedirectedOverride: false);

        Assert.Equal(ExitCodes.ErrorParseArguments, exitCode);
        Assert.Contains("streamPipedInput", stderr.ToString());
    }
}
