// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using CSharpRepl.Services;
using CSharpRepl.Services.Roslyn.References;
using System;
using Xunit;

namespace CSharpRepl.Tests;

public class CommandLineTests
{
    [Fact]
    public void ParseArguments_NoArguments_ProducesDefaultConfiguration()
    {
        var result = Parse(null);
        Assert.NotNull(result);
        Assert.Equal(SharedFramework.NetCoreApp, result.Framework);
    }

    [Theory]
    [InlineData("-f"), InlineData("--framework"), InlineData("/f")]
    public void ParseArguments_FrameworkArgument_SpecifiesFramework(string flag)
    {
        var result = Parse($"{flag} Microsoft.AspNetCore.App");
        Assert.NotNull(result);
        Assert.Equal("Microsoft.AspNetCore.App", result.Framework);

        var concatenatedValue = Parse($"/f:Microsoft.AspNetCore.App");
        Assert.Equal("Microsoft.AspNetCore.App", concatenatedValue.Framework);
    }

    [Theory]
    [InlineData("-u"), InlineData("--using"), InlineData("/u")]
    public void ParseArguments_UsingArguments_ProducesUsings(string flag)
    {
        var result = Parse($"{flag} System.Linq System.Data Newtonsoft.Json");
        Assert.NotNull(result);
        Assert.Equal(new[] { "System.Linq", "System.Data", "Newtonsoft.Json" }, result.Usings);

        var concatenatedValues = Parse($"/u:System.Linq /u:System.Data /u:Newtonsoft.Json");
        Assert.Equal(new[] { "System.Linq", "System.Data", "Newtonsoft.Json" }, concatenatedValues.Usings);
    }

    [Theory]
    [InlineData("-r"), InlineData("--reference"), InlineData("/r")]
    public void ParseArguments_ReferencesArguments_ProducesUsings(string flag)
    {
        var result = Parse($"{flag} Foo.dll Bar.dll");
        Assert.NotNull(result);
        Assert.Equal(new[] { "Foo.dll", "Bar.dll" }, result.References);

        var concatenatedValues = Parse($"/r:Foo.dll /r:Bar.dll");
        Assert.Equal(new[] { "Foo.dll", "Bar.dll" }, concatenatedValues.References);
    }

    [Theory]
    [InlineData("-t"), InlineData("--theme"), InlineData("/t")]
    public void ParseArguments_ThemeArguments_SpecifiesTheme(string flag)
    {
        var result = Parse($"{flag} Data/theme.json");
        Assert.NotNull(result);
        Assert.Equal(41, result.Theme.SyntaxHighlightingColors.Length);
        Assert.True(result.Theme.TryGetSyntaxHighlightingColor("struct name", out var color));
        Assert.Equal("Yellow", color.ToString());
    }

    [Theory]
    [InlineData("-v"), InlineData("--version"), InlineData("/v")]
    public void ParseArguments_VersionArguments_SpecifiesVersion(string flag)
    {
        var result = Parse(flag);
        Assert.NotNull(result);
        Assert.Contains("C# REPL ", result.OutputForEarlyExit);
    }

    [Theory]
    [InlineData("-h"), InlineData("--help"), InlineData("/h"), InlineData("/?")]
    public void ParseArguments_HelpArguments_SpecifiesHelp(string flag)
    {
        var result = Parse(flag);
        Assert.NotNull(result);
        Assert.Contains("Usage: ", result.OutputForEarlyExit);
    }

    [Fact]
    public void ParseArguments_ComplexCommandLine_ProducesConfiguration()
    {
        var result = Parse("-t Data/theme.json -u System.Linq System.Data -u Newtonsoft.Json --reference foo.dll -f Microsoft.AspNetCore.App --reference bar.dll baz.dll Data/LoadScript.csx");
        Assert.NotNull(result);

        Assert.Equal(41, result.Theme.SyntaxHighlightingColors.Length);
        Assert.True(result.Theme.TryGetSyntaxHighlightingColor("struct name", out var color));
        Assert.Equal("Yellow", color.ToString());

        Assert.Equal(new[] { "System.Linq", "System.Data", "Newtonsoft.Json" }, result.Usings);
        Assert.Equal(new[] { "foo.dll", "bar.dll", "baz.dll" }, result.References);
        Assert.Equal("Microsoft.AspNetCore.App", result.Framework);
        Assert.Equal(@"Console.WriteLine(""Hello World!"");" + Environment.NewLine, result.LoadScript);
    }

    [Fact]
    public void ParseArguments_ResponseFile_ProducesConfiguration()
    {
        var result = Parse("@Data/ResponseFile.rsp");

        Assert.NotNull(result);
        Assert.Equal(SharedFramework.NetCoreApp, result.Framework);
        Assert.Equal(new[] { "System", "System.Linq", "Foo.Main.Text" }, result.Usings);
        Assert.Equal(new[] { "System", "System.ValueTuple.dll", "Foo.Main.Logic.dll", "lib.dll" }, result.References);
    }

    [Fact]
    public void ParseArguments_TrailingArgumentsAfterDoubleDash_SetAsLoadScriptArgs()
    {
        var csxResult = CommandLine.Parse(new[] { "Data/LoadScript.csx", "--", "Data/LoadScript.csx" });
        // load script filename passed before "--" is a load script, after "--" we just pass it to the load script as an arg.
        Assert.Equal(new[] { "Data/LoadScript.csx" }, csxResult.LoadScriptArgs);
        Assert.Equal(@"Console.WriteLine(""Hello World!"");" + Environment.NewLine, csxResult.LoadScript);

        var quotedResult = CommandLine.Parse(new[] { "-r", "Foo.dll", "--", @"""a b c""", @"""d e f""" });
        Assert.Equal(new[] { @"""a b c""", @"""d e f""" }, quotedResult.LoadScriptArgs);
        Assert.Equal(new[] { @"Foo.dll" }, quotedResult.References);
    }

    [Fact]
    public void ParseArguments_DotNetSuggestFrameworkParameter_IsAutocompleted()
    {
        var result = Parse("[suggest:3] --f");
        Assert.Equal("--framework" + Environment.NewLine, result.OutputForEarlyExit);
    }

    [Fact]
    public void ParseArguments_DotNetSuggestFrameworkValue_IsAutocompleted()
    {
        var result = CommandLine.Parse(new[] { "[suggest:12]", "--framework " });
        Assert.Contains("Microsoft.NETCore.App", result.OutputForEarlyExit);
    }

    [Fact]
    public void ParseArguments_DotNetSuggestUsingValue_IsAutocompleted()
    {
        var result = CommandLine.Parse(new[] { "[suggest:25]", "--using System.Collection" });
        Assert.Contains("System.Collections.Immutable", result.OutputForEarlyExit);
    }

    private static Configuration Parse(string commandline) =>
        CommandLine.Parse(commandline?.Split(' ') ?? Array.Empty<string>());
}
