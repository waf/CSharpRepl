// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

#nullable enable

using System;
using System.Diagnostics;
using System.Linq;
using System.Text;
using CSharpRepl.Services;
using CSharpRepl.Services.Roslyn.Formatting;
using CSharpRepl.Services.SyntaxHighlighting;
using CSharpRepl.Services.Theming;
using Microsoft.Extensions.Caching.Memory;
using Spectre.Console;
using Spectre.Console.Rendering;
using Spectre.Console.Testing;
using Xunit;

[assembly: DebuggerDisplay("AssemblyTargeted: {Name,nq}", Target = typeof(CSharpRepl.Tests.ObjectFormatting.DebuggerDisplayTests.AssemblyTargeted))]

namespace CSharpRepl.Tests.ObjectFormatting;

/// <summary>
/// Integration-style tests for the [DebuggerDisplay]/[DebuggerTypeProxy] formatting pipeline:
/// objects flow through PrettyPrinter.FormatObjectToText / FormatMembers, which drive
/// FormatWithEmbeddedExpressions and the ObjectFormatterHelpers member resolution
/// (ParseSimpleMemberName, ResolveMember, GetMemberValue, GetDebuggerTypeProxy).
/// </summary>
public class DebuggerDisplayTests
{
    private readonly PrettyPrinter prettyPrinter;

    public DebuggerDisplayTests()
    {
        prettyPrinter = new PrettyPrinter(
            new TestConsole().Profile,
            new SyntaxHighlighter(
                new MemoryCache(new MemoryCacheOptions()),
                new Theme(null, null, null, null, [])),
            new Configuration());
    }

    private string Format(object obj) => prettyPrinter.FormatObjectToText(obj, Level.FirstSimple).ToString();

    private string[] FormatMembers(object obj) =>
        prettyPrinter.FormatMembers(obj, Level.FirstSimple, includeNonPublic: false)
            .Select(member => Render(member.Renderable))
            .ToArray();

    [Fact]
    public void PropertyFieldMethodAndNoQuotesExpressions()
    {
        // {Name} property (quoted), {age} private field, {Tag,nq} unquoted string, {GetVolume()} method call
        Assert.Equal(@"Person: ""Alice"", Age = 42, Tag = tagged, Volume = 11", Format(new Person()));
    }

    [Fact]
    public void ExpressionsToleratePaddingWhitespace()
    {
        Assert.Equal("Alice|11", Format(new PaddedExpressions()));
    }

    [Fact]
    public void MemberResolutionIsCaseInsensitiveWhenExactMatchFails()
    {
        // {name} resolves to Name; {getanswer()} resolves to GetAnswer()
        Assert.Equal(@"""by-property"" 21", Format(new CaseInsensitiveMembers()));
    }

    [Fact]
    public void AmbiguousCaseInsensitiveMatchIsAnError()
    {
        Assert.Equal("!<Member 'ambiguous' not found>", Format(new AmbiguousMembers()));
    }

    [Fact]
    public void MissingAndThrowingMembersRenderErrors()
    {
        Assert.Equal(
            "!<Member 'Missing' not found> / !<Method 'MissingMethod' not found> / !<InvalidOperationException>",
            Format(new WithErrors()));
    }

    [Fact]
    public void SetOnlyPropertyIsNotResolvable()
    {
        Assert.Equal("!<Member 'WriteOnly' not found>", Format(new WithSetOnlyProperty()));
    }

    [Fact]
    public void MalformedAndEscapedBracesRenderLiterally()
    {
        // \{ is an escaped brace; an unclosed { swallows the rest of the format string verbatim
        Assert.Equal(@"\{notexpr} {Unclosed", Format(new MalformedFormat()));
    }

    [Fact]
    public void MemberInheritedFromBaseClassIsResolved()
    {
        Assert.Equal("7", Format(new DerivedFromBase()));
    }

    [Fact]
    public void AssemblyLevelAttributeWithTargetTypeApplies()
    {
        Assert.Equal("AssemblyTargeted: from-assembly-attribute", Format(new AssemblyTargeted()));
    }

    [Fact]
    public void ToStringOverrideIsUsedWhenNoDebuggerDisplay()
    {
        Assert.Equal("custom tostring", Format(new ToStringOnly()));
    }

    [Fact]
    public void ThrowingToStringRendersError()
    {
        Assert.Equal("!<ApplicationException>", Format(new ToStringThrows()));
    }

    [Fact]
    public void DebuggerTypeProxyReplacesMembers()
    {
        Assert.Equal(["ProxyValue: 99"], FormatMembers(new ProxiedValue()));
    }

    [Fact]
    public void GenericDebuggerTypeProxyIsConstructedForGenericTarget()
    {
        Assert.Equal([@"Display: ""proxied!"""], FormatMembers(new Box<int> { Value = 1 }));
    }

    [Fact]
    public void ThrowingDebuggerTypeProxyFallsBackToRealMembers()
    {
        Assert.Equal(["RealValue: 5"], FormatMembers(new WithThrowingProxy()));
    }

    [Fact]
    public void DebuggerDisplayNameOnMemberRenamesIt()
    {
        var member = Assert.Single(FormatMembers(new WithNamedMember()));
        Assert.StartsWith("RenamedX: ", member);
    }

    [Fact]
    public void DebuggerBrowsableNeverHidesAndRootHiddenFlattensMembers()
    {
        Assert.Equal(["InnerValue: 2"], FormatMembers(new WithBrowsableMembers()));
    }

    private static string Render(IRenderable renderable)
    {
        const int Width = 1000;
        var options = new RenderOptions(new TestCapabilities(), new Size(Width, 1000));
        var sb = new StringBuilder();
        foreach (var segment in renderable.Render(options, Width))
        {
            sb.Append(segment.Text);
        }
        return sb.ToString();
    }

#pragma warning disable IDE0051, IDE0052, CS0414 // members are accessed via reflection by the formatter
    [DebuggerDisplay("Person: {Name}, Age = {age}, Tag = {Tag,nq}, Volume = {GetVolume()}")]
    private class Person
    {
        private readonly int age = 42;
        public string Tag = "tagged";
        public string Name => "Alice";
        public int GetVolume() => 11;
    }

    [DebuggerDisplay("{Name, nq}|{ GetVolume ( ) }")]
    private class PaddedExpressions
    {
        public string Name => "Alice";
        public int GetVolume() => 11;
    }

    [DebuggerDisplay("{name} {getanswer()}")]
    private class CaseInsensitiveMembers
    {
        public string Name => "by-property";
        public int GetAnswer() => 21;
    }

    [DebuggerDisplay("{ambiguous}")]
    private class AmbiguousMembers
    {
        public int Ambiguous = 1;
        public int AMBIGUOUS => 2;
    }

    [DebuggerDisplay("{Missing} / {MissingMethod()} / {Throws}")]
    private class WithErrors
    {
        public int Throws => throw new InvalidOperationException("oops");
    }

    [DebuggerDisplay("{WriteOnly}")]
    private class WithSetOnlyProperty
    {
        public int WriteOnly { set { } }
    }

    [DebuggerDisplay(@"\{notexpr} {Unclosed")]
    private class MalformedFormat
    {
    }

    private class BaseWithMember
    {
        public int BaseValue = 7;
    }

    [DebuggerDisplay("{BaseValue}")]
    private class DerivedFromBase : BaseWithMember
    {
    }

    public class AssemblyTargeted
    {
        public string Name => "from-assembly-attribute";
    }

    private class ToStringOnly
    {
        public override string ToString() => "custom tostring";
    }

    private class ToStringThrows
    {
        public override string ToString() => throw new ApplicationException();
    }

    [DebuggerTypeProxy(typeof(ValueProxy))]
    private class ProxiedValue
    {
        public string Hidden = "real";
    }

    private class ValueProxy
    {
        public ValueProxy(ProxiedValue value) { }
        public int ProxyValue => 99;
    }

    [DebuggerTypeProxy(typeof(BoxProxy<>))]
    private class Box<T>
    {
        public T? Value;
    }

    private class BoxProxy<T>
    {
        public BoxProxy(Box<T> box) { }
        public string Display => "proxied!";
    }

    [DebuggerTypeProxy(typeof(ThrowingProxy))]
    private class WithThrowingProxy
    {
        public int RealValue = 5;
    }

    private class ThrowingProxy
    {
        public ThrowingProxy(WithThrowingProxy value) => throw new InvalidOperationException("broken proxy");
    }

    private class WithNamedMember
    {
        [DebuggerDisplay("val", Name = "Renamed{Suffix,nq}")]
        public NamedMemberValue Item => new();

        public class NamedMemberValue
        {
            public string Suffix => "X";
        }
    }

    private class WithBrowsableMembers
    {
        [DebuggerBrowsable(DebuggerBrowsableState.Never)]
        public int Hidden = 1;

        [DebuggerBrowsable(DebuggerBrowsableState.RootHidden)]
        public Wrapped Root = new();

        public class Wrapped
        {
            public int InnerValue = 2;
        }
    }
#pragma warning restore IDE0051, IDE0052, CS0414
}
