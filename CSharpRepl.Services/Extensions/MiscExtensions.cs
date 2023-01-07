using System.Diagnostics.CodeAnalysis;

namespace CSharpRepl.Services.Extensions;

internal static class MiscExtensions
{
    public static bool TryGet<T>(this T? nullableValue, out T value)
        where T : struct
    {
        if (nullableValue.HasValue)
        {
            value = nullableValue.GetValueOrDefault();
            return true;
        }
        else
        {
            value = default;
            return false;
        }
    }

    public static bool TryGet<T>(this T? nullableValue, [MaybeNullWhen(false)] out T value)
        where T : class
    {
        if (nullableValue is null)
        {
            value = null;
            return false;
        }
        else
        {
            value = nullableValue;
            return true;
        }
    }
}