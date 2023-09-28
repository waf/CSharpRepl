using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using CSharpRepl.Services.Roslyn;
using CSharpRepl.Services.Roslyn.Formatting;
using CSharpRepl.Services.Roslyn.Formatting.CustomObjectFormatters;
using CSharpRepl.Services.Roslyn.Scripting;
using Xunit;

namespace CSharpRepl.Tests.ObjectFormatting;

public class CustomObjectFormattersTests : IClassFixture<RoslynServicesFixture>
{
    private readonly TestFormatter formatter;
    private readonly RoslynServices services;

    public CustomObjectFormattersTests(RoslynServicesFixture fixture)
    {
        formatter = TestFormatter.Create(fixture.ConsoleStub);
        services = fixture.RoslynServices;
    }

    #region TypeFormatter
    [Theory]
    [InlineData(typeof(int), "System.Int32", "int")]
    [InlineData(typeof(List<int>), "System.Collections.Generic.List<System.Int32>", "List<int>")]
    [InlineData(typeof(Dictionary<int, List<Enum>>), "System.Collections.Generic.Dictionary<System.Int32, System.Collections.Generic.List<System.Enum>>", "Dictionary<int, List<Enum>>")]
    public void TestTypeFormatting(Type value, string expectedOutput_0, string expectedOutput_1)
    {
        Assert.Equal(expectedOutput_0, formatter.Format(TypeFormatter.Instance, value, Level.FirstDetailed));
        Assert.Equal(expectedOutput_1, formatter.Format(TypeFormatter.Instance, value, Level.FirstSimple));
    }

    [Theory]
    [InlineData("class Class1 { } new Class1()", "Class1")] //https://github.com/waf/CSharpRepl/issues/287
    [InlineData("class Class2<T> { } new Class2<int>()", "Class2<int>")] //https://github.com/waf/CSharpRepl/issues/305
    public async Task TypeDefinedInsideReplFormattingBug(string input, string expectedOutput)
    {
        var eval = await services.EvaluateAsync(input);
        if (eval is EvaluationResult.Success { ReturnValue.Value: object obj })
        {
            Assert.Equal(expectedOutput, formatter.Format(TypeFormatter.Instance, obj.GetType(), Level.FirstSimple));
        }
        else
        {
            Assert.Fail();
        }
    }
    #endregion

    #region MethodInfoFormatter
    [Theory]
    [MemberData(nameof(MethodInfoData))]
    public void TestMethodInfoFormatting(MethodInfo value, string expectedOutput_0_Detailed, string expectedOutput_0_Simple, string expectedOutput_1, string expectedOutput_2)
    {
        Assert.Equal(expectedOutput_0_Detailed, formatter.Format(MethodInfoFormatter.Instance, value, Level.FirstDetailed));
        Assert.Equal(expectedOutput_0_Simple, formatter.Format(MethodInfoFormatter.Instance, value, Level.FirstSimple));
        Assert.Equal(expectedOutput_1, formatter.Format(MethodInfoFormatter.Instance, value, Level.Second));
        Assert.Equal(expectedOutput_2, formatter.Format(MethodInfoFormatter.Instance, value, Level.ThirdPlus));
    }

    public static IEnumerable<object[]> MethodInfoData
    {
        get
        {
            Func<string, NumberStyles, IFormatProvider, int> intParse = int.Parse;
            yield return new object[]
            {
                intParse.Method,
                "public static System.Int32 Parse(System.String s, System.Globalization.NumberStyles style, System.IFormatProvider provider)",
                "public static int Parse(string s, NumberStyles style, IFormatProvider provider)",
                "int Parse(string, NumberStyles, IFormatProvider)",
                "Parse"
            };

            yield return new object[]
            {
                typeof(Enum)
                    .GetMethods(BindingFlags.Static | BindingFlags.Public)
                    .Single(m => m is { Name: "TryParse", IsGenericMethod: true } && m.GetParameters() is [{ ParameterType.Name: "ReadOnlySpan`1" }, { }]),
                "public static System.Boolean TryParse<TEnum>(System.ReadOnlySpan<System.Char> value, TEnum& result)",
                "public static bool TryParse<TEnum>(ReadOnlySpan<char> value, TEnum& result)",
                "bool TryParse<TEnum>(ReadOnlySpan<char>, TEnum&)",
                "TryParse"
            };

            yield return new object[]
            {
                typeof(TestClass)
                    .GetMethod($"CSharpRepl.Tests.ObjectFormatting.CustomObjectFormattersTests.ITestInterface.M", BindingFlags.Instance | BindingFlags.NonPublic),
                "private virtual void CSharpRepl.Tests.ObjectFormatting.CustomObjectFormattersTests.ITestInterface.M()",
                "private virtual void ITestInterface.M()",
                "void M()",
                "M"
            };
        }
    }

    private class TestClass : ITestInterface
    {
        void ITestInterface.M() { }
    }

    private interface ITestInterface
    {
        void M();
    }
    #endregion

    #region IEnumerableFormatter
    [Theory]
    [MemberData(nameof(IEnumerableData))]
    public void TestIEnumerableFormatting(IEnumerable value, string expectedOutput_0, string expectedOutput_1)
    {
        Assert.Equal(expectedOutput_0, formatter.Format(IEnumerableFormatter.Instance, value, Level.FirstDetailed));
        Assert.Equal(expectedOutput_1, formatter.Format(IEnumerableFormatter.Instance, value, Level.FirstSimple));
    }

    public static IEnumerable<object[]> IEnumerableData
    {
        get
        {
            yield return new object[]
            {
                new[] { 1, 2, 3 },
                "int[3] { 1, 2, 3 }",
                "int[3] { 1, 2, 3 }",
            };

            yield return new object[]
            {
                new[] { typeof(int), typeof(string) },
                "Type[2] { int, string }",
                "Type[2] { int, string }",
            };

            yield return new object[]
            {
                new object[] { 2, new[] { 1, 2, 3 }, typeof(List<int>) },
                "object[3] { 2, int[3] { 1, 2, 3 }, List<int> }",
                "object[3] { 2, int[3] { 1, 2, 3 }, List<int> }",
            };
        }
    }
    #endregion

    #region TupleFormatter
    [Theory]
    [MemberData(nameof(TupleData))]
    public void TestTupleFormatting(ITuple value, string expectedOutput_0, string expectedOutput_1)
    {
        Assert.Equal(expectedOutput_0, formatter.Format(TupleFormatter.Instance, value, Level.FirstDetailed));
        Assert.Equal(expectedOutput_1, formatter.Format(TupleFormatter.Instance, value, Level.FirstSimple));
    }

    public static IEnumerable<object[]> TupleData
    {
        get
        {
            yield return new object[]
            {
                (1, 2, 3),
                "(1, 2, 3)",
                "(1, 2, 3)",
            };

            yield return new object[]
            {
                (typeof(int), typeof(string)),
                "(System.Int32, System.String)",
                "(int, string)",
            };

            yield return new object[]
            {
                (2, new[] { 1, 2, 3 }, typeof(List<int>)),
                "(2, int[3] { 1, 2, 3 }, System.Collections.Generic.List<System.Int32>)",
                "(2, int[3] { 1, 2, 3 }, List<int>)",
            };
        }
    }
    #endregion

    #region KeyValuePair
    [Theory]
    [MemberData(nameof(KeyValuePairData))]
    public void TestKeyValuePairFormatting(object value, string expectedOutput_0, string expectedOutput_1, string expectedOutput_2)
    {
        Assert.Equal(expectedOutput_0, formatter.Format(KeyValuePairFormatter.Instance, value, Level.FirstDetailed));
        Assert.Equal(expectedOutput_1, formatter.Format(KeyValuePairFormatter.Instance, value, Level.FirstSimple));
        Assert.Equal(expectedOutput_2, formatter.Format(KeyValuePairFormatter.Instance, value, Level.Second));
    }

    public static IEnumerable<object[]> KeyValuePairData
    {
        get
        {
            yield return new object[]
            {
                KeyValuePair.Create(1, 2),
                "KeyValuePair<int, int> { 1, 2 }",
                "{ Key: 1, Value: 2 }",
                "{ 1, 2 }",
            };

            yield return new object[]
            {
                KeyValuePair.Create(typeof(int), typeof(string)),
                "KeyValuePair<Type, Type> { System.Int32, System.String }",
                "{ Key: int, Value: string }",
                "{ int, string }",
            };

            yield return new object[]
            {
                KeyValuePair.Create(new[] { 1, 2, 3 }, typeof(List<int>)),
                "KeyValuePair<int[], Type> { int[3] { 1, 2, 3 }, System.Collections.Generic.List<System.Int32> }",
                "{ Key: int[3] { 1, 2, 3 }, Value: List<int> }",
                "{ int[3] { 1, 2, 3 }, List<int> }",
            };
        }
    }
    #endregion

    #region Guid
    [Fact]
    public void TestGuidFormatting()
    {
        var guid = Guid.NewGuid();
        Assert.Equal(guid.ToString(), formatter.Format(GuidFormatter.Instance, guid, Level.FirstDetailed));
    }
    #endregion
}