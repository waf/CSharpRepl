// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.IO;
using System.Reflection;
using System.Runtime.Versioning;
using NuGet.Frameworks;
using NuGet.RuntimeModel;

namespace CSharpRepl.Services.Nuget;

internal static class NugetHelper
{
    private const string RuntimeFileName = "runtime.json";

    public static RuntimeGraph GetRuntimeGraph(Action<string>? error)
    {
        var dir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
        if (dir != null)
        {
            var path = Path.Combine(dir, RuntimeFileName);
            if (File.Exists(path))
            {
                using var stream = File.OpenRead(path);
                return JsonRuntimeFormat.ReadRuntimeGraph(stream);
            }
        }
        error?.Invoke($"Cannot find '{RuntimeFileName}' in '{dir}'");
        return new RuntimeGraph();
    }

    public static NuGetFramework GetCurrentFramework()
    {
        var assembly = Assembly.GetEntryAssembly();
        if (assembly is null ||
            assembly.Location.EndsWith("testhost.dll", StringComparison.OrdinalIgnoreCase)) //for unit tests (testhost.dll targets netcoreapp2.1 instead of net6.0)
        {
            assembly = Assembly.GetExecutingAssembly();
        }

        var targetFrameworkAttribute = assembly.GetCustomAttribute<TargetFrameworkAttribute>();
        return NuGetFramework.Parse(targetFrameworkAttribute?.FrameworkName);
    }
}