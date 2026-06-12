#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using CSharpRepl.Services;
using CSharpRepl.Services.Roslyn;
using CSharpRepl.Services.Roslyn.Formatting;
using CSharpRepl.Services.Roslyn.Scripting;
using CSharpRepl.Services.SyntaxHighlighting;
using CSharpRepl.Services.Theming;
using Microsoft.Extensions.Caching.Memory;
using Spectre.Console;
using Spectre.Console.Rendering;
using Spectre.Console.Testing;
using Xunit;
using static System.Environment;

namespace CSharpRepl.Tests.ObjectFormatting;

public class PrettyPrinterTests : IClassFixture<RoslynServicesFixture>
{
    private readonly FakeConsoleAbstract console;
    private readonly RoslynServices services;
    private readonly PrettyPrinter prettyPrinter;

    public PrettyPrinterTests(RoslynServicesFixture fixture)
    {
        console = fixture.ConsoleStub;
        services = fixture.RoslynServices;
        prettyPrinter = new PrettyPrinter(
            console.AnsiConsole.Profile,
            new SyntaxHighlighter(
                new MemoryCache(new MemoryCacheOptions()),
                new Theme(null, null, null, null, [])),
            new Configuration());
    }

    [Theory]
    [InlineData(@"throw null;", $"NullReferenceException: Object reference not set to an instance of an object.")]
    [InlineData(@"void M1() => M2(); void M2() => throw null; M1()", $"NullReferenceException: Object reference not set to an instance of an object.\n    at void M2()\n    at void M1()")]
    [InlineData(@"async Task M1() => await M2(); async Task M2() => throw null; await M1()", $"NullReferenceException: Object reference not set to an instance of an object.\n    at async Task M2()\n    at async Task M1()")]
    [InlineData(@"void M((int A, int B) tuple) => throw null; M(default)", $"NullReferenceException: Object reference not set to an instance of an object.\n    at void M((int A, int B) tuple)")]
    [InlineData(@"class Ctor { public Ctor() => throw null; } new Ctor()", $"NullReferenceException: Object reference not set to an instance of an object.\n    at new Ctor()")]
    [InlineData(@"int M() { int Local() => throw null; return Local(); } M()", $"NullReferenceException: Object reference not set to an instance of an object.\n    at int M()+Local()\n    at int M()")]
    [InlineData(@"void M<T>() => throw null; M<int>()", $"NullReferenceException: Object reference not set to an instance of an object.\n    at void M<T>()")]
    [InlineData(@"void M(ref int x) => throw null; int y = 0; M(ref y)", $"NullReferenceException: Object reference not set to an instance of an object.\n    at void M(ref int x)")]
    [InlineData(@"void M(dynamic d) => throw null; M(1)", $"NullReferenceException: Object reference not set to an instance of an object.\n    at void M(dynamic d)")]
    [InlineData(@"void M(int n) { if (n == 0) throw null; M(n - 1); } M(3)", $"NullReferenceException: Object reference not set to an instance of an object.\n    at void M(int n) x 4")]
    public async Task ExceptionCallstack(string input, string expectedOutput)
    {
        var result = await services.EvaluateAsync(input, cancellationToken: TestContext.Current.CancellationToken);
        var exception = ((EvaluationResult.Error)result).Exception;
        var output = prettyPrinter.FormatException(exception, Level.FirstDetailed).ToString();

        Assert.Equal(expectedOutput.Replace("\n", NewLine), output);
    }

    [Fact]
    public async Task ExceptionCallstack_Lambda_RendersLambdaMarker()
    {
        var result = await services.EvaluateAsync("Func<int> f = () => throw null; f()", cancellationToken: TestContext.Current.CancellationToken);
        var exception = ((EvaluationResult.Error)result).Exception;

        var output = prettyPrinter.FormatException(exception, Level.FirstDetailed).ToString();

        Assert.Contains("=> { }", output); // the demystified lambda frame
    }

    /// <summary>
    /// Exceptions thrown from regular (non-script) assemblies have PDB info, so their frames
    /// render with file paths and line numbers.
    /// </summary>
    [Fact]
    public void ExceptionCallstack_WithPdbInfo_IncludesFileAndLineNumbers()
    {
        Exception exception;
        try
        {
            Deep1();
            throw new InvalidOperationException("unreachable");
        }
        catch (InvalidOperationException e)
        {
            exception = e;
        }

        var output = prettyPrinter.FormatException(exception, Level.FirstDetailed).ToString();

        Assert.Contains("InvalidOperationException: deep failure", output);
        Assert.Contains("PrettyPrinterTests.Deep6()", output);
        Assert.Contains(".cs:line ", output);
    }

    // deep enough that some frames survive FormatException's trimming of the bottommost
    // (normally script-runner) frames.
    [MethodImpl(MethodImplOptions.NoInlining)] private static int Deep1() => Deep2();
    [MethodImpl(MethodImplOptions.NoInlining)] private static int Deep2() => Deep3();
    [MethodImpl(MethodImplOptions.NoInlining)] private static int Deep3() => Deep4();
    [MethodImpl(MethodImplOptions.NoInlining)] private static int Deep4() => Deep5();
    [MethodImpl(MethodImplOptions.NoInlining)] private static int Deep5() => Deep6();
    [MethodImpl(MethodImplOptions.NoInlining)] private static int Deep6() => throw new InvalidOperationException("deep failure");

    /// <summary>
    /// https://github.com/waf/CSharpRepl/issues/193
    /// </summary>
    [Theory]
    [InlineData(Level.FirstDetailed)]
    [InlineData(Level.FirstSimple)]
    internal async Task CompilationErrorException(Level level)
    {
        var result = await services.EvaluateAsync("+", cancellationToken: TestContext.Current.CancellationToken);
        var exception = ((EvaluationResult.Error)result).Exception;
        var output = ToString(prettyPrinter.FormatObject(exception, level).Renderable);
        Assert.Equal("(1,2): error CS1733: Expected expression", output);
    }

    [Theory]
    [MemberData(nameof(FormatObjectInputs))]
    internal void FormatObject_ObjectInput_PrintsOutput(object? obj, Level level, string expectedResult)
    {
        var output =
            obj is Array or List<int> ?
            prettyPrinter.FormatObjectToText(obj, level).ToString() :
            ToString(prettyPrinter.FormatObject(obj, level).Renderable);
        Assert.Equal(expectedResult, output);
    }

    public static IEnumerable<object?[]> FormatObjectInputs =>
    [
        new object?[] { null, Level.FirstSimple, "null" },
        [null, Level.FirstDetailed, "null"],

        [@"""hello world""", Level.FirstSimple, @"""\""hello world\"""""],
        [@"""hello world""", Level.FirstDetailed, @"""hello world"""],

        ["a\nb", Level.FirstSimple, @"""a\nb"""],
        ["a\nb", Level.FirstDetailed, "a\nb"],

        [new[] { 1, 2, 3 }, Level.FirstSimple, "int[3] { 1, 2, 3 }"],
        [new[] { 1, 2, 3 }, Level.FirstDetailed, "int[3] { 1, 2, 3 }"],

        [new List<int>{ 1, 2, 3 }, Level.FirstSimple, "List<int>(3) { 1, 2, 3 }"],
        [new List<int>{ 1, 2, 3 }, Level.FirstDetailed, "List<int>(3) { 1, 2, 3 }"],

        [typeof(List<int>.Enumerator), Level.FirstSimple, "List<int>.Enumerator"],
        [typeof(List<int>.Enumerator), Level.FirstDetailed, "System.Collections.Generic.List<System.Int32>.Enumerator"],

        [typeof(int), Level.FirstSimple, "int"],
        [typeof(int), Level.FirstDetailed, "System.Int32"],

        [typeof(int[]), Level.FirstSimple, "int[]"],
        [typeof(int[]), Level.FirstDetailed, "System.Int32[]"],

        [typeof(int[,]), Level.FirstSimple, "int[,]"],
        [typeof(int[,]), Level.FirstDetailed, "System.Int32[,]"],

        [typeof(int[][]), Level.FirstSimple, "int[][]"],
        [typeof(int[][]), Level.FirstDetailed, "System.Int32[][]"],

        [typeof((int, int)), Level.FirstSimple, "(int, int)"],
        [typeof((int, int)), Level.FirstDetailed, "System.ValueTuple<System.Int32, System.Int32>"],

        [typeof(int?), Level.FirstSimple, "int?"],
        [typeof(int?), Level.FirstDetailed, "System.Nullable<System.Int32>"],

        [Encoding.UTF8, Level.FirstDetailed, "System.Text.UTF8Encoding.UTF8EncodingSealed"],

        // primitive / scalar values exercise PrimitiveFormatter's per-type literal formatting.
        // Numbers are formatted identically regardless of detail level.
        [(byte)200, Level.FirstSimple, "200"],
        [(byte)200, Level.FirstDetailed, "200"],
        [(sbyte)-5, Level.FirstSimple, "-5"],
        [(sbyte)-5, Level.FirstDetailed, "-5"],
        [(short)-300, Level.FirstSimple, "-300"],
        [(short)-300, Level.FirstDetailed, "-300"],
        [(ushort)300, Level.FirstSimple, "300"],
        [(ushort)300, Level.FirstDetailed, "300"],
        [42, Level.FirstSimple, "42"],
        [42, Level.FirstDetailed, "42"],
        [42u, Level.FirstSimple, "42"],
        [42u, Level.FirstDetailed, "42"],
        [42L, Level.FirstSimple, "42"],
        [42L, Level.FirstDetailed, "42"],
        [42UL, Level.FirstSimple, "42"],
        [42UL, Level.FirstDetailed, "42"],
        [3.14, Level.FirstSimple, "3.14"],
        [3.14, Level.FirstDetailed, "3.14"],
        [1.5f, Level.FirstSimple, "1.5"],
        [1.5f, Level.FirstDetailed, "1.5"],
        [2.5m, Level.FirstSimple, "2.5"],
        [2.5m, Level.FirstDetailed, "2.5"],
        [true, Level.FirstSimple, "true"],
        [false, Level.FirstSimple, "false"],
        ['a', Level.FirstSimple, "'a'"],
        ['a', Level.FirstDetailed, "'a'"],
        ['\n', Level.FirstSimple, @"'\n'"],
        [DayOfWeek.Monday, Level.FirstSimple, "Monday"],
        [DayOfWeek.Monday, Level.FirstDetailed, "Monday"],

        // string and char escaping goes through the vendored ObjectDisplay.FormatLiteral.
        ["tab\tquote\"backslash\\", Level.FirstSimple, "\"tab\\tquote\\\"backslash\\\\\""],
        ["null\0alert\abackspace\bformfeed\fvertical\v", Level.FirstSimple, @"""null\0alert\abackspace\bformfeed\fvertical\v"""],
        ["control\u0001char", Level.FirstSimple, @"""control\u0001char"""],
        ["unpaired\ud800surrogate", Level.FirstSimple, @"""unpaired\ud800surrogate"""],
        ["emoji \U0001F600 pair", Level.FirstSimple, "\"emoji \U0001F600 pair\""],
        ["noncharacter \U0010FFFF", Level.FirstSimple, @"""noncharacter \U0010ffff"""],
        ['\'', Level.FirstSimple, @"'\''"],
        ['\0', Level.FirstSimple, @"'\0'"],
        ['\u0001', Level.FirstSimple, @"'\u0001'"],
        [double.NaN, Level.FirstSimple, "NaN"],
        [float.NaN, Level.FirstSimple, "NaN"],

        // multidimensional and jagged arrays go through IEnumerableFormatter.FormatToText.
        [new int[,] { { 1, 2 }, { 3, 4 } }, Level.FirstSimple, "int[4] { 1, 2, 3, 4 }"],
        [new int[,] { { 1, 2 }, { 3, 4 } }, Level.FirstDetailed, "int[4] { 1, 2, 3, 4 }"],
        [new int[][] { [1, 2], [3] }, Level.FirstSimple, "int[][2] { int[2] { 1, 2 }, int[1] { 3 } }"],
        [new int[][] { [1, 2], [3] }, Level.FirstDetailed, "int[][2] { int[2] { 1, 2 }, int[1] { 3 } }"],
    ];

    /// <summary>
    /// At first-level detail an <see cref="IEnumerable"/> is rendered as a Name/Value/Type table
    /// (IEnumerableFormatter.FormatToRenderable) rather than the inline "{ ... }" text form.
    /// </summary>
    [Theory]
    [InlineData(Level.FirstSimple, @"{ ""a"", 1 }")]
    [InlineData(Level.FirstDetailed, @"{ Key: ""a"", Value: 1 }")]
    public void FormatObject_Dictionary_RendersAsTableWithEntries(Level level, string expectedValueCell)
    {
        var dictionary = new Dictionary<string, int> { ["a"] = 1, ["b"] = 2 };

        var output = ToString(prettyPrinter.FormatObject(dictionary, level).Renderable);

        Assert.Contains("Dictionary<string, int>(2)", output); // header with element count
        Assert.Contains("KeyValuePair<string, int>", output);  // Type column
        Assert.Contains(expectedValueCell, output);            // Value column entry
    }

    [Theory]
    [MemberData(nameof(ObjectMembersFormattingInputs))]
    public void TestObjectMembersFormatting(object obj, Level level, string[] expectedResults, bool includeNonPublic)
    {
        var outputs = prettyPrinter.FormatMembers(obj, level, includeNonPublic).ToArray();
        Assert.Equal(expectedResults.Length, outputs.Length);
        foreach (var (output, expectedResult) in outputs.Zip(expectedResults))
        {
            Assert.Equal(expectedResult, ToString(output.Renderable));
        }
    }

    private class TestClassWithMembers
    {
#pragma warning disable IDE0051, IDE0052 // Remove unread private members
        private readonly object fieldObject = new();
        private string PropertyString => "abcd";
#pragma warning restore IDE0051, IDE0052 // Remove unread private members

        public int FieldInt32 = 2;
        public virtual decimal PropertyDecimal1 { get; } = 2;
        public virtual decimal PropertyDecimal2 { get; } = 3;
        public virtual decimal PropertyDecimal3 { get; } = 5;
        public decimal PropertyDecimal4 { get; } = 7;
    }

    private class TestClassWithMembersDerived : TestClassWithMembers
    {
        //https://github.com/waf/CSharpRepl/issues/229
        public override decimal PropertyDecimal1 => 11;
        public override sealed decimal PropertyDecimal2 => 13;
        public new decimal PropertyDecimal4 { get; } = 17;
    }

    private class TestClassWithMembersDerived2 : TestClassWithMembersDerived
    {
        //https://github.com/waf/CSharpRepl/issues/229
        public override decimal PropertyDecimal1 => 19;
    }

    public static IEnumerable<object[]> ObjectMembersFormattingInputs =>
    [
        new object[] { new(), Level.FirstDetailed, Array.Empty<string>(), false },
        [new(), Level.FirstDetailed, Array.Empty<string>(), true],

        [new TestClassWithMembers(), Level.FirstSimple, new[] { "FieldInt32: 2", "PropertyDecimal1: 2", "PropertyDecimal2: 3", "PropertyDecimal3: 5", "PropertyDecimal4: 7" }, false],
        [new TestClassWithMembers(), Level.FirstDetailed, new[] { "FieldInt32: 2", "fieldObject: object", "PropertyDecimal1: 2", "PropertyDecimal2: 3", "PropertyDecimal3: 5", "PropertyDecimal4: 7", "PropertyString: \"abcd\"" }, true],

        [new TestClassWithMembersDerived2(), Level.FirstSimple, new[] { "FieldInt32: 2", "PropertyDecimal1: 19", "PropertyDecimal2: 13", "PropertyDecimal3: 5", "PropertyDecimal4: 17", "PropertyDecimal4: 7" }, false],
        [new TestClassWithMembersDerived2(), Level.FirstDetailed, new[] { "FieldInt32: 2", "fieldObject: object", "PropertyDecimal1: 19", "PropertyDecimal2: 13", "PropertyDecimal3: 5", "PropertyDecimal4: 17", "PropertyDecimal4: 7", "PropertyString: \"abcd\"" }, true],
    ];

    private static string ToString(IRenderable renderable)
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
}
