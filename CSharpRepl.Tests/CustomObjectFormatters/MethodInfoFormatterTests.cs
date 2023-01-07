using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using CSharpRepl.Services.Roslyn.CustomObjectFormatters;
using Xunit;

namespace CSharpRepl.Tests;

public class MethodInfoFormatterTests
{
    private readonly TestFormatter formatter = TestFormatter.Create();

    [Theory]
    [MemberData(nameof(ParseStyleData))]
    public void ExceptionCallstack(MethodInfo value, string expectedOutput_0, string expectedOutput_1, string expectedOutput_2)
    {
        Assert.Equal(expectedOutput_0, formatter.Format(MethodInfoFormatter.Instance, value, level: 0));
        Assert.Equal(expectedOutput_1, formatter.Format(MethodInfoFormatter.Instance, value, level: 1));
        Assert.Equal(expectedOutput_2, formatter.Format(MethodInfoFormatter.Instance, value, level: 2));
    }

    public static IEnumerable<object[]> ParseStyleData
    {
        get
        {
            Func<string, NumberStyles, IFormatProvider, int> intParse = int.Parse;
            yield return new object[]
            {
                intParse.Method,
                "public static int Parse(string s, System.Globalization.NumberStyles style, System.IFormatProvider provider)",
                "int Parse(string, NumberStyles, IFormatProvider)",
                "Parse"
            };

            yield return new object[]
            {
                typeof(Enum)
                    .GetMethods(BindingFlags.Static | BindingFlags.Public)
                    .Single(m => m is { Name: "TryParse", IsGenericMethod: true } && m.GetParameters() is [{ ParameterType.Name: "ReadOnlySpan`1" }, { }]),
                "public static bool TryParse<TEnum>(System.ReadOnlySpan<char> value, TEnum& result)",
                "bool TryParse<TEnum>(ReadOnlySpan<char>, TEnum&)",
                "TryParse"
            };

            yield return new object[]
            {
                typeof(TestClass)
                    .GetMethod($"CSharpRepl.Tests.MethodInfoFormatterTests.ITestInterface.M", BindingFlags.Instance | BindingFlags.NonPublic),
                "private virtual void CSharpRepl.Tests.MethodInfoFormatterTests.ITestInterface.M()",
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
}