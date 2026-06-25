// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Reflection;
using System.Runtime.Versioning;
using NuGet.Frameworks;
using NuGet.RuntimeModel;

namespace CSharpRepl.Services.Nuget;

internal static class NugetHelper
{
    private const string RuntimeFileName = "runtime.json";

    /// <summary>
    /// Absolute path to the shipped <c>runtime.json</c> RID graph, or <c>null</c> if it isn't present. Used as the
    /// restore's <see cref="NuGet.ProjectModel.TargetFrameworkInformation.RuntimeIdentifierGraphPath"/> so that
    /// RID-specific package assets (runtimes/&lt;rid&gt;/...) resolve.
    /// </summary>
    public static string? RuntimeGraphPath
    {
        get
        {
            var dir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            if (dir is null) return null;
            var path = Path.Combine(dir, RuntimeFileName);
            return File.Exists(path) ? path : null;
        }
    }

    public static RuntimeGraph GetRuntimeGraph(Action<string>? error)
    {
        var path = RuntimeGraphPath;
        if (path != null)
        {
            using var stream = File.OpenRead(path);
            return JsonRuntimeFormat.ReadRuntimeGraph(stream);
        }
        error?.Invoke($"Cannot find '{RuntimeFileName}'");
        return new RuntimeGraph();
    }

    public static bool TryGetCurrentFramework([NotNullWhen(true)] out NuGetFramework? result)
    {
        var assembly = Assembly.GetEntryAssembly();
        if (assembly is null ||
            assembly.Location.EndsWith("testhost.dll", StringComparison.OrdinalIgnoreCase)) //for unit tests (testhost.dll targets netcoreapp2.1 instead of net6.0)
        {
            assembly = Assembly.GetExecutingAssembly();
        }

        var targetFrameworkAttribute = assembly.GetCustomAttribute<TargetFrameworkAttribute>();
        if (targetFrameworkAttribute is null)
        {
            result = null;
            return false;
        }

        result = NuGetFramework.Parse(targetFrameworkAttribute.FrameworkName);
        return true;
    }
}