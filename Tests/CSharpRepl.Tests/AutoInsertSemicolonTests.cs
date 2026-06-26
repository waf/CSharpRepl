// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

#nullable enable

using CSharpRepl.Services.Roslyn.Scripting;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace CSharpRepl.Tests;

/// <summary>
/// Unit tests for the issue #356 single-line auto-semicolon rule. These are pure (no Roslyn workspace/fixture):
/// the rule only needs the same Script <see cref="CSharpParseOptions"/> that <c>RoslynServices</c> uses.
/// </summary>
public class AutoInsertSemicolonTests
{
    private static readonly CSharpParseOptions ParseOptions =
        CSharpParseOptions.Default.WithKind(SourceCodeKind.Script).WithLanguageVersion(LanguageVersion.Latest);

    [Theory]
    // single-line declaration the user didn't terminate -> complete, and TryAppend adds the ';'
    [InlineData("int i = 0", true)]
    [InlineData("var x = 5", true)]
    [InlineData("int j", true)]
    [InlineData("int a = 1, b = 2", true)]
    [InlineData("var x = Enumerable.Range(1, 5)", true)]
    // already-complete submissions -> complete, but nothing to append
    [InlineData("var x = 5;", false)]
    [InlineData("1 + 1", false)]
    [InlineData("if (x == 4) return;", false)]
    // genuinely incomplete / not a single-line field declaration -> neither complete nor appended
    [InlineData("var x = ", false)]
    [InlineData("if (x == 4)", false)]
    [InlineData("int Square(int x)", false)]
    [InlineData("var x = Enumerable\n.Range(1, 5)", false)]
    [InlineData("if you're happy and you know it, syntax error!", false)]
    public void TryAppend_AppendsSemicolonOnlyForUnterminatedSingleLineDeclarations(string code, bool shouldAppend)
    {
        var appended = AutoInsertSemicolon.TryAppend(code, ParseOptions);

        if (shouldAppend)
            Assert.Equal(code.Trim() + ";", appended);
        else
            Assert.Null(appended);
    }
}
