// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Collections.Generic;
using System.Threading.Tasks;
using PrettyPrompt.Completion;
using Xunit;

namespace CSharpRepl.Tests;

public partial class RoslynServicesTests
{
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

    [Theory]
    [InlineData("new string(", "'x')", "")]
    [InlineData("new string(", "'x', 123)", "")]
    [InlineData("new C(", "\"x\", 123)", "\nclass C { public C(string s){} public C(string s, int x){} }")]
    public async Task Complete_Overloads_NewInstance(string text, string argsAll, string suffix)
    {
        for (int argsStart = 0; argsStart < argsAll.Length - 1; argsStart++)
            for (int argsLen = 1; argsLen < argsAll.Length - argsStart - 1; argsLen++)
            {
                var args = argsAll.Substring(argsStart, argsLen);
                var code = $"{text}{args}{suffix}";
                var overloadsHelpStart = text.Length;

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

    [Theory]
    [InlineData("\"abc\"[", "123]", "")]
    public async Task Complete_Overloads_Indexer(string text, string argsAll, string suffix)
    {
        for (int argsStart = 0; argsStart < argsAll.Length - 1; argsStart++)
            for (int argsLen = 1; argsLen < argsAll.Length - argsStart - 1; argsLen++)
            {
                var args = argsAll.Substring(argsStart, argsLen);
                var code = $"{text}{args}{suffix}";
                var overloadsHelpStart = text.Length;

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

    [Theory]
    [InlineData("new Dictionary<", "string, int>", "", 1)]
    [InlineData("new Dictionary<", "string, int>", "()", 1)]
    [InlineData("new System.Collections.Generic.Dictionary<", "string, int>", "", 1)]
    [InlineData("new System . Collections . Generic . Dictionary <", " string , int >", "", 1)]
    [InlineData("new C<", "string, int>", "\nclass C<TKey>{}\nclass C<TKey, K>{}", 2)]
    public async Task Complete_Overloads_GenericArgs_Constructor(string text, string typeArgsAll, string suffix, int overloadCount)
    {
        for (int typeArgsStart = 0; typeArgsStart < typeArgsAll.Length - 1; typeArgsStart++)
            for (int typeArgsLen = 1; typeArgsLen < typeArgsAll.Length - typeArgsStart - 1; typeArgsLen++)
            {
                var typeArgs = typeArgsAll.Substring(typeArgsStart, typeArgsLen);
                var code = $"{text}{typeArgs}{suffix}";
                var overloadsHelpStart = text.Length;

                (IReadOnlyList<OverloadItem> Overloads, int ArgumentIndex) result;
                for (int caret = 0; caret < overloadsHelpStart; caret++)
                {
                    result = await services.GetOverloadsAsync(text + typeArgs, caret, cancellationToken: default);
                    Assert.Empty(result.Overloads);
                    Assert.Equal(0, result.ArgumentIndex);
                }

                for (int caret = overloadsHelpStart; caret < code.Length; caret++)
                {
                    result = await services.GetOverloadsAsync(code, caret: overloadsHelpStart, cancellationToken: default);
                    Assert.True(result.Overloads.Count == overloadCount);
                    Assert.Equal(0, result.ArgumentIndex);
                    foreach (var overload in result.Overloads)
                    {
                        Assert.Contains("<TKey", overload.Signature.Text);
                    }
                }
            }
    }

    /// <summary>
    /// https://github.com/waf/CSharpRepl/issues/163
    /// </summary>
    [Fact]
    public async Task Complete_MethodWithoutXmlDoc()
    {
        var code = "M();\nvoid M(){}";
        var result = await services.GetOverloadsAsync(code, caret: 2, cancellationToken: default);
        Assert.True(result.Overloads.Count == 1);
        Assert.Equal(0, result.ArgumentIndex);
        Assert.Contains("M", result.Overloads[0].Signature.Text);
    }
}