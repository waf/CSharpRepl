#nullable enable

using System;
using CSharpRepl.Services;
using CSharpRepl.Services.Roslyn.CustomObjectFormatters;
using CSharpRepl.Services.SyntaxHighlighting;
using CSharpRepl.Services.Theming;
using Microsoft.CodeAnalysis.CSharp.Scripting.Hosting;
using Microsoft.CodeAnalysis.Scripting.Hosting;
using Microsoft.Extensions.Caching.Memory;
using Xunit;

namespace CSharpRepl.Tests.ObjectFormatting;

internal class TestFormatter : CSharpObjectFormatterImpl
{
    public TestFormatter(SyntaxHighlighter highlighter, Configuration config)
        : base(highlighter, config)
    { }

    public string Format(ICustomObjectFormatter formatter, object value, Level level, PrintOptions? options = null)
    {
        options ??= new PrintOptions();

        var visitor = new Visitor(
                    this,
                    TypeNameFormatter,
                    GetInternalBuilderOptions(options),
                    GetPrimitiveOptions(options),
                    GetTypeNameOptions(options),
                    options.MemberDisplayFormat,
                    highlighter,
                    configuration);

        Assert.True(formatter.IsApplicable(value));
        return formatter.Format(value, level, new Formatter(visitor)).ToString();
    }

    public static TestFormatter Create() => new(
        new SyntaxHighlighter(
            new MemoryCache(new MemoryCacheOptions()),
            new Theme(null, null, null, null, Array.Empty<SyntaxHighlightingColor>())),
        new Configuration());
}