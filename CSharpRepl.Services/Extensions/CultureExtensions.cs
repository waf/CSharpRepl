using System;
using System.Globalization;

namespace CSharpRepl.Services.Extensions;

public static class CultureExtensions
{
    public static CultureInfo? CultureInfoByName(string? cultureName)
    {
        if (!string.IsNullOrWhiteSpace(cultureName))
        {
            foreach (var culture in CultureInfo.GetCultures(CultureTypes.AllCultures))
            {
                if (string.Equals(culture.Name, cultureName, StringComparison.OrdinalIgnoreCase))
                {
                    return culture;
                }
            }
        }

        return null;
    }
}