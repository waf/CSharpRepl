using System;
using CSharpRepl.Services;
using CSharpRepl.Services.Roslyn.CustomObjectFormatters;
using CSharpRepl.Services.SyntaxHighlighting;
using CSharpRepl.Services.Theming;
using Microsoft.CodeAnalysis.CSharp.Scripting.Hosting;
using Microsoft.CodeAnalysis.Scripting.Hosting;
using Microsoft.Extensions.Caching.Memory;
using Xunit;

namespace CSharpRepl.Tests;

internal class TestFormatter : CSharpObjectFormatterImpl
{
    private readonly Visitor visitor;

    public TestFormatter(SyntaxHighlighter highlighter, Configuration config)
        : base(highlighter, config)
    {
        var options = new PrintOptions();
        visitor = new Visitor(this, TypeNameFormatter, GetInternalBuilderOptions(options), GetPrimitiveOptions(options), GetTypeNameOptions(options), options.MemberDisplayFormat, highlighter, config);
    }

    public string Format(ICustomObjectFormatter formatter, object value, int level)
    {
        Assert.True(formatter.IsApplicable(value));
        return formatter.Format(value, level, visitor).ToString();
    }

    public static TestFormatter Create() => new(
        new SyntaxHighlighter(
            new MemoryCache(new MemoryCacheOptions()),
            new Theme(null, null, null, null, Array.Empty<SyntaxHighlightingColor>())),
        new Configuration());
}