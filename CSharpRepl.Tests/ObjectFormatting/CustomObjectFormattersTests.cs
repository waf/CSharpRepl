using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using CSharpRepl.Services.Roslyn.CustomObjectFormatters;
using Xunit;

namespace CSharpRepl.Tests.ObjectFormatting;

public class CustomObjectFormattersTests
{
    private readonly TestFormatter formatter = TestFormatter.Create();

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
                (2, new[] { 1, 2, 3 }, typeof(List<int>)),
                "(2, int[3] { 1, 2, 3 }, System.Collections.Generic.List<System.Int32>)",
                "(2, int[3] { 1, 2, 3 }, List<int>)",
            };
        }
    }
    #endregion
}