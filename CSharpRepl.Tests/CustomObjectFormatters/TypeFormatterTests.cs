using System;
using System.Collections.Generic;
using CSharpRepl.Services.Roslyn.CustomObjectFormatters;
using Xunit;

namespace CSharpRepl.Tests;

public class TypeFormatterTests
{
    private readonly TestFormatter formatter = TestFormatter.Create();

    [Theory]
    [InlineData(typeof(int), "System.Int32", "int")]
    [InlineData(typeof(List<int>), "System.Collections.Generic.List<System.Int32>", "List<int>")]
    [InlineData(typeof(Dictionary<int, List<Enum>>), "System.Collections.Generic.Dictionary<System.Int32, System.Collections.Generic.List<System.Enum>>", "Dictionary<int, List<Enum>>")]
    public void ExceptionCallstack(Type value, string expectedOutput_0, string expectedOutput_1)
    {
        Assert.Equal(expectedOutput_0, formatter.Format(TypeFormatter.Instance, value, level: 0));
        Assert.Equal(expectedOutput_1, formatter.Format(TypeFormatter.Instance, value, level: 1));
    }
}