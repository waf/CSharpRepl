// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using PrettyPrompt.Completion;
using Xunit;

namespace CSharpRepl.Tests;

public partial class RoslynServicesTests
{
    //Method invocation overloads.
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

    //New instance (constructor overloads).
    [InlineData("new string(", "'x')", "", 9, "string.String(")]
    [InlineData("new string(", "'x', 123)", "", 9, "string.String(")]
    [InlineData("new C(", "\"x\", 123)", "\nclass C { public C(string s){} public C(string s, int x){} }", 2, "C(")]

    //Indexer overloads.
    [InlineData("\"abc\"[", "123]", "", 1, "string.this[")]

    //New instance (generic overloads).
    [InlineData("new Dictionary<", "string, int>", "", 1, "Dictionary<")]
    [InlineData("new Dictionary<", "string, int>", "()", 1, "Dictionary<")]
    [InlineData("new System.Collections.Generic.Dictionary<", "string, int>", "", 1, "Dictionary<")]
    [InlineData("new System . Collections . Generic . Dictionary <", " string , int >", "", 1, "Dictionary<")]
    [InlineData("new C<", "string, int>", "\nclass C<TKey>{}\nclass C<TKey, K>{}", 2, "C<")]

    //Method invocation (generic overloads).
    [InlineData("M<", "string>", "\nvoid M<T1>(){}\nvoid M<T1, T2>(){}", 2, "M<")]
    [InlineData("M<", "string, int>", "\nvoid M<T1>(){}\nvoid M<T1, T2>(){}", 2, "M<")]
    [InlineData("M<", "string, int>", "();\nvoid M<T1>(){}\nvoid M<T1, T2>(){}", 2, "M<")]
    [InlineData("default(C).M<", "string, int>", "();\nclass C { public void M<T1>() { }\npublic void M<T1, T2>() { } }", 2, "M<")]
    [InlineData("C.M<", "string, int>", "();\nclass C { public static void M<T1>() { }\npublic static void M<T1, T2>() { } }", 2, "M<")]

    //Type (generic overloads).
    [InlineData("MyType<", "string>", "\nclass MyType<T1>{}\nclass MyType<T1, T2>{}", 2, "MyType<")]
    [InlineData("MyType<", "string, int>", "\nclass MyType<T1>{}\nclass MyType<T1, T2>{}", 2, "MyType<")]
    [InlineData("MyType<", "string, int>", ".Equals();\nclass MyType<T1>{}\nclass MyType<T1, T2>{}", 2, "MyType<")]
    [InlineData("Dictionary<", "string, char>", "", 1, "Dictionary<")]
    [InlineData("Dictionary<", "int, char>", "", 1, "Dictionary<")]
    [InlineData("void M(Dictionary<", "int, char>", "", 1, "Dictionary<")]
    public async Task TestOverloads(string text, string argsAll, string suffix, int overloadCount, string signaturePart)
    {
        Assert.True(signaturePart.EndsWith('(') || signaturePart.EndsWith('[') || signaturePart.EndsWith('<'));
        var complementaryOpeningTokens = new[] { '(', '[', '<' }.Where(c => c != signaturePart.Last()).ToArray();

        for (int argsLen = 0; argsLen <= argsAll.Length; argsLen++)
        {
            var args = argsAll[..argsLen];
            if (suffix.Length > 0 && args.Length != argsAll.Length) continue;

            var code = $"{text}{args}{suffix}";
            var overloadsHelpStart = text.Length;

            var secondArgStartCaret = code.IndexOf(',');
            secondArgStartCaret = secondArgStartCaret == -1 ? int.MaxValue : secondArgStartCaret + 1;

            (IReadOnlyList<OverloadItem> Overloads, int ArgumentIndex) result;
            for (int caret = 0; caret < overloadsHelpStart; caret++)
            {
                result = await services.GetOverloadsAsync(code, caret, cancellationToken: default);
                Assert.Empty(result.Overloads);
                Assert.Equal(0, result.ArgumentIndex);
            }

            for (int caret = overloadsHelpStart; caret < code.Length - suffix.Length; caret++)
            {
                result = await services.GetOverloadsAsync(code, caret, cancellationToken: default);
                Assert.Equal(overloadCount, result.Overloads.Count);
                Assert.Equal(caret < secondArgStartCaret ? 0 : 1, result.ArgumentIndex);
                foreach (var overload in result.Overloads)
                {
                    Assert.Contains(signaturePart, overload.Signature.Text);
                    Assert.True(overload.Parameters.Count > 0);
                }
            }

            if (argsLen == argsAll.Length)
            {
                for (int caret = code.Length - suffix.Length; caret <= code.Length; caret++)
                {
                    result = await services.GetOverloadsAsync(code, caret, cancellationToken: default);
                    Assert.True(result.Overloads.All(o => complementaryOpeningTokens.Any(c => o.Signature.Text.Contains(c))));
                    Assert.Equal(0, result.ArgumentIndex);
                }
            }
        }
    }

    [Theory]
    [InlineData("Dictionary<List<")]
    [InlineData("Dictionary<HashSet<List<")]
    [InlineData("new Dictionary<List<")]
    public async Task Complete_RecursiveGenerics(string code)
    {
        var result = await services.GetOverloadsAsync(code, caret: code.Length, cancellationToken: default);
        Assert.True(result.Overloads.Count == 1);
        Assert.Equal(0, result.ArgumentIndex);
        Assert.Contains("List", result.Overloads[0].Signature.Text);
        Assert.Equal(1, result.Overloads[0].Parameters.Count);
        Assert.Equal("T", result.Overloads[0].Parameters[0].Name);
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
        Assert.Empty(result.Overloads[0].Parameters);
    }

    /// <summary>
    /// https://github.com/waf/CSharpRepl/issues/164
    /// </summary>
    [Fact]
    public async Task Complete_ParamWithoutXmlDoc()
    {
        var code = "M();\nvoid M(int i){}";
        var result = await services.GetOverloadsAsync(code, caret: 2, cancellationToken: default);
        Assert.True(result.Overloads.Count == 1);
        Assert.Equal(0, result.ArgumentIndex);
        var overload = result.Overloads[0];
        Assert.Contains("M", overload.Signature.Text);
        Assert.Equal(1, overload.Parameters.Count);
        Assert.Equal("i", overload.Parameters[0].Name);
    }

    /// <summary>
    /// https://github.com/waf/CSharpRepl/issues/164
    /// </summary>
    [Fact]
    public async Task Complete_ParamsWithAndWithoutXmlDoc()
    {
        var code = "M();\n/// <summary>desc</summary> <param name=\"b\">b desc</param>\nvoid M(int a, string b){}";
        var result = await services.GetOverloadsAsync(code, caret: 2, cancellationToken: default);
        Assert.True(result.Overloads.Count == 1);
        Assert.Equal(0, result.ArgumentIndex);
        var overload = result.Overloads[0];
        Assert.Contains("M", overload.Signature.Text);
        Assert.Contains("desc", overload.Summary.Text);
        Assert.Equal(2, overload.Parameters.Count);

        Assert.Equal("a", overload.Parameters[0].Name);
        Assert.Equal("", overload.Parameters[0].Description.Text);

        Assert.Equal("b", overload.Parameters[1].Name);
        Assert.Equal("b desc", overload.Parameters[1].Description.Text);
    }

}