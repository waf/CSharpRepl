using System.Text.RegularExpressions;

namespace CSharpRepl.Tests;

internal static class Extensions
{
    private static readonly Regex AnsiEscapeCodeRegex = new(@"\u001b\[.+?m");

    public static string RemoveFormatting(this string input) => AnsiEscapeCodeRegex.Replace(input, "");
}