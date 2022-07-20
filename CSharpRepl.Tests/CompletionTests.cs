// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CSharpRepl.Services;
using CSharpRepl.Services.Roslyn;
using PrettyPrompt.Completion;
using Xunit;

namespace CSharpRepl.Tests;

[Collection(nameof(RoslynServices))]
public class CompletionTests : IAsyncLifetime
{
    private readonly RoslynServices services;

    public CompletionTests()
    {
        var (console, _) = FakeConsole.CreateStubbedOutput();
        this.services = new RoslynServices(console, new Configuration(), new TestTraceLogger());
    }

    public Task InitializeAsync() => services.WarmUpAsync(Array.Empty<string>());
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Complete_GivenCode_ReturnsCompletions()
    {
        var completions = await this.services.CompleteAsync("Console.Writ", 12);
        var writelines = completions
            .Where(c => c.Item.DisplayText.StartsWith("Write"))
            .ToList();

        Assert.Equal("Write", writelines[0].Item.DisplayText);
        Assert.Equal("WriteLine", writelines[1].Item.DisplayText);

        var writeDescription = await writelines[0].GetDescriptionAsync(cancellationToken: default);
        Assert.Contains("Writes the text representation of the specified", writeDescription.Text);
        var writeLineDescription = await writelines[1].GetDescriptionAsync(cancellationToken: default);
        Assert.Contains("Writes the current line terminator to the standard output", writeLineDescription.Text);
    }

    [Fact]
    public async Task Complete_GivenLinq_ReturnsCompletions()
    {
        // LINQ tends to be a good canary for whether or not our reference / implementation assemblies are correct.
        var completions = await this.services.CompleteAsync("new[] { 1, 2, 3 }.Wher", 21);

        var whereCompletion = completions.SingleOrDefault(c => c.Item.DisplayText.StartsWith("Where"));

        Assert.NotNull(whereCompletion);
        Assert.Equal("Where", whereCompletion.Item.DisplayText);

        var whereDescription = await whereCompletion.GetDescriptionAsync(cancellationToken: default);
        Assert.Contains("Filters a sequence of values based on a predicate", whereDescription.Text);
    }

    /// <summary>
    /// https://github.com/waf/CSharpRepl/issues/4
    /// </summary>
    [Fact]
    public async Task Complete_SyntaxHighlight_CachesAreIsolated()
    {
        // type "c" which triggers completion at index 1, and is cached
        var completions = await this.services.CompleteAsync("c", 1);

        // next, type the number 1, which could collide with the previous cached value if the caches
        // aren't isolated, resulting in an exception
        var highlights = await this.services.SyntaxHighlightAsync("c1");

        Assert.NotEmpty(completions);
        Assert.NotEmpty(highlights);
    }

    /// <summary>
    /// https://github.com/waf/CSharpRepl/issues/65
    /// </summary>
    [Fact]
    public async Task Complete_GetDescriptionForShorterCompletion()
    {
        var completions = await this.services.CompleteAsync("datetime", 8);
        var arrayCompletion = completions.SingleOrDefault(c => c.Item.DisplayText == "Array");
        Assert.NotNull(arrayCompletion);
        var arrayDescription = await arrayCompletion.GetDescriptionAsync(cancellationToken: default);
        Assert.Contains("Provides methods for creating, manipulating, searching, and sorting arrays", arrayDescription.Text);
    }

    /// <summary>
    /// https://github.com/waf/CSharpRepl/issues/92
    /// </summary>
    [Fact]
    public async Task Complete_GetDescriptionAfterDot()
    {
        var completions = await this.services.CompleteAsync("\"\".Where()", 3);
        var whereCompletion = completions.SingleOrDefault(c => c.Item.DisplayText == "Where");
        Assert.NotNull(whereCompletion);
        var whereDescription = await whereCompletion.GetDescriptionAsync(cancellationToken: default);
        Assert.Contains("Filters a sequence of values based on a predicate", whereDescription.Text);
    }

    [Fact]
    public async Task Complete_Overloads()
    {
        for (int whiteSpacesCount = 0; whiteSpacesCount < 3; whiteSpacesCount++)
        {
            var _ = new string(' ', whiteSpacesCount);
            var code = $"{_}Math{_}.{_}Max{_}({_}";
            var overloadsHelpStart = code.Length - _.Length;

            (IReadOnlyList<OverloadItem> Overloads, int ArgumentIndex) result;
            for (int caret = 0; caret < overloadsHelpStart; caret++)
            {
                result = await services.GetOverloadsAsync(code, caret, cancellationToken: default);
                Assert.Empty(result.Overloads);
                Assert.Equal(0, result.ArgumentIndex);
            }

            result = await services.GetOverloadsAsync(code, caret: overloadsHelpStart, cancellationToken: default);
            Assert.True(result.Overloads.Count > 0);
            Assert.Equal(0, result.ArgumentIndex);
            foreach (var overload in result.Overloads)
            {
                Assert.Contains("Max", overload.Signature.Text);
            }

            //////////////////////////////////////////////////////////////////////////////////////////////////////////

            code = $"{_}Math{_}.{_}Max{_}({_}){_};{_}";
            overloadsHelpStart = code.Length - $"{_}){_};{_}".Length;
            var overloadsHelpEndExclusive = code.Length - $"{_};{_}".Length;

            for (int caret = 0; caret < overloadsHelpStart; caret++)
            {
                result = await services.GetOverloadsAsync(code, caret, cancellationToken: default);
                Assert.Empty(result.Overloads);
                Assert.Equal(0, result.ArgumentIndex);
            }

            for (int caret = overloadsHelpStart; caret < overloadsHelpEndExclusive; caret++)
            {
                result = await services.GetOverloadsAsync(code, caret, cancellationToken: default);
                Assert.True(result.Overloads.Count > 0);
                Assert.Equal(0, result.ArgumentIndex);
                Assert.Equal(0, result.ArgumentIndex);
                foreach (var overload in result.Overloads)
                {
                    Assert.Contains("Max", overload.Signature.Text);
                }
            }

            for (int caret = overloadsHelpEndExclusive; caret <= code.Length; caret++)
            {
                result = await services.GetOverloadsAsync(code, caret, cancellationToken: default);
                Assert.Empty(result.Overloads);
                Assert.Equal(0, result.ArgumentIndex);
            }

            //////////////////////////////////////////////////////////////////////////////////////////////////////////

            code = $"{_}Math{_}.{_}Max{_}({_}123,{_}456{_}){_};{_}";
            overloadsHelpStart = code.Length - $"{_}123,{_}456{_}){_};{_}".Length;
            overloadsHelpEndExclusive = code.Length - $"{_};{_}".Length;

            for (int caret = 0; caret < overloadsHelpStart; caret++)
            {
                result = await services.GetOverloadsAsync(code, caret, cancellationToken: default);
                Assert.Empty(result.Overloads);
                Assert.Equal(0, result.ArgumentIndex);
            }

            for (int caret = overloadsHelpStart; caret < overloadsHelpEndExclusive; caret++)
            {
                result = await services.GetOverloadsAsync(code, caret, cancellationToken: default);
                Assert.True(result.Overloads.Count > 0);
                Assert.Equal(caret <= $"{_}Math{_}.{_}Max{_}({_}123".Length ? 0 : 1, result.ArgumentIndex);
                foreach (var overload in result.Overloads)
                {
                    Assert.Contains("Max", overload.Signature.Text);
                }
            }

            for (int caret = overloadsHelpEndExclusive; caret <= code.Length; caret++)
            {
                result = await services.GetOverloadsAsync(code, caret, cancellationToken: default);
                Assert.Empty(result.Overloads);
                Assert.Equal(0, result.ArgumentIndex);
            }

            //////////////////////////////////////////////////////////////////////////////////////////////////////////

            code = $"{_}Math{_}.{_}Max{_}({_}Math{_}.{_}Abs{_}({_}-123{_}),{_}456{_}){_};{_}";
            overloadsHelpStart = code.Length - $"{_}Math{_}.{_}Abs{_}({_}-123{_}),{_}456{_}){_};{_}".Length;
            overloadsHelpEndExclusive = code.Length - $"{_}-123{_}),{_}456{_}){_};{_}".Length;

            for (int caret = 0; caret < overloadsHelpStart; caret++)
            {
                result = await services.GetOverloadsAsync(code, caret, cancellationToken: default);
                Assert.Empty(result.Overloads);
                Assert.Equal(0, result.ArgumentIndex);
            }

            for (int caret = overloadsHelpStart; caret < overloadsHelpEndExclusive; caret++)
            {
                result = await services.GetOverloadsAsync(code, caret, cancellationToken: default);
                Assert.True(result.Overloads.Count > 0);
                Assert.Equal(caret <= $"{_}Math{_}.{_}Max{_}({_}Math{_}.{_}Abs{_}({_}-123{_})".Length ? 0 : 1, result.ArgumentIndex);
                foreach (var overload in result.Overloads)
                {
                    Assert.Contains("Max", overload.Signature.Text);
                }
            }

            //abs arg list start
            overloadsHelpStart = overloadsHelpEndExclusive;
            overloadsHelpEndExclusive = code.Length - $",{_}456{_}){_};{_}".Length;
            for (int caret = overloadsHelpStart; caret < overloadsHelpEndExclusive; caret++)
            {
                result = await services.GetOverloadsAsync(code, caret, cancellationToken: default);
                Assert.True(result.Overloads.Count > 0);
                Assert.Equal(0, result.ArgumentIndex);
                foreach (var overload in result.Overloads)
                {
                    Assert.Contains("Abs", overload.Signature.Text);
                }
            }
            //abs arg list end

            overloadsHelpStart = overloadsHelpEndExclusive;
            overloadsHelpEndExclusive = code.Length - $"{_};{_}".Length;
            for (int caret = overloadsHelpStart; caret < overloadsHelpEndExclusive; caret++)
            {
                result = await services.GetOverloadsAsync(code, caret, cancellationToken: default);
                Assert.True(result.Overloads.Count > 0);
                Assert.Equal(caret <= $"{_}Math{_}.{_}Max{_}({_}Math{_}.{_}Abs{_}({_}-123{_})".Length ? 0 : 1, result.ArgumentIndex);
                foreach (var overload in result.Overloads)
                {
                    Assert.Contains("Max", overload.Signature.Text);
                }
            }

            for (int caret = overloadsHelpEndExclusive; caret <= code.Length; caret++)
            {
                result = await services.GetOverloadsAsync(code, caret, cancellationToken: default);
                Assert.Empty(result.Overloads);
                Assert.Equal(0, result.ArgumentIndex);
            }
        }
    }

    [Fact]
    public async Task Complete_Overloads_NewInstance()
    {
        var code = $"new string('x'";
        var overloadsHelpStart = $"new string(".Length;

        (IReadOnlyList<OverloadItem> Overloads, int ArgumentIndex) result;
        for (int caret = 0; caret < overloadsHelpStart; caret++)
        {
            result = await services.GetOverloadsAsync(code, caret, cancellationToken: default);
            Assert.Empty(result.Overloads);
            Assert.Equal(0, result.ArgumentIndex);
        }

        for (int caret = overloadsHelpStart; caret < code.Length; caret++)
        {
            result = await services.GetOverloadsAsync(code, caret: overloadsHelpStart, cancellationToken: default);
            Assert.True(result.Overloads.Count > 0);
            Assert.Equal(0, result.ArgumentIndex);
            foreach (var overload in result.Overloads)
            {
                Assert.Contains("string", overload.Signature.Text);
            }
        }
    }

    [Fact]
    public async Task Complete_Overloads_Indexer()
    {
        var code = $"\"abc\"[0";
        var overloadsHelpStart = $"\"abc\"[".Length;

        (IReadOnlyList<OverloadItem> Overloads, int ArgumentIndex) result;
        for (int caret = 0; caret < overloadsHelpStart; caret++)
        {
            result = await services.GetOverloadsAsync(code, caret, cancellationToken: default);
            Assert.Empty(result.Overloads);
            Assert.Equal(0, result.ArgumentIndex);
        }

        for (int caret = overloadsHelpStart; caret < code.Length; caret++)
        {
            result = await services.GetOverloadsAsync(code, caret: overloadsHelpStart, cancellationToken: default);
            Assert.True(result.Overloads.Count > 0);
            Assert.Equal(0, result.ArgumentIndex);
            foreach (var overload in result.Overloads)
            {
                Assert.Contains("string", overload.Signature.Text);
            }
        }
    }
}