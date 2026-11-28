// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using Microsoft.Build.Locator;

namespace CSharpRepl.Services.Roslyn.References;

/// <summary>
/// Guards the features that load MSBuild / NuGet assemblies from the installed .NET SDK at run time (NuGet package
/// references, project/solution references, deps.json expansion), so they fail with an actionable message instead of
/// a <see cref="FileNotFoundException"/> for e.g. <c>NuGet.Versioning</c>.
/// <para>
/// Those assemblies aren't shipped with csharprepl; they only resolve after <see cref="MSBuildLocator.RegisterDefaults"/>
/// (called by <see cref="DotNetInstallationLocator"/>) has registered an SDK. MSBuildLocator ignores SDKs whose major
/// version is higher than the runtime csharprepl runs on, because their assemblies target that newer runtime. So when
/// the only installed SDK is a preview of the next major version, there's nothing to register: csharprepl's
/// <c>RollForward=LatestMajor</c> skips prerelease runtimes, so it keeps running on the older release runtime, which
/// can't use the preview SDK. https://github.com/waf/CSharpRepl/issues/526
/// </para>
/// <para>
/// Possible mitigation for users, UNTESTED: setting <c>DOTNET_ROLL_FORWARD_TO_PRERELEASE=1</c> should let csharprepl
/// roll forward onto the preview runtime that ships with the preview SDK, and MSBuildLocator would then accept that SDK.
/// This hasn't been verified, so it's deliberately left out of the user-facing message below.
/// </para>
/// </summary>
internal static class DotNetSdkRequirement
{
    /// <summary>
    /// Whether a compatible .NET SDK was registered, i.e. whether MSBuild and NuGet assemblies can be loaded.
    /// </summary>
    public static bool IsAvailable => MSBuildLocator.IsRegistered;

    /// <summary>
    /// Throws an <see cref="InvalidOperationException"/> explaining how to fix the installation if no compatible .NET
    /// SDK was registered. Call this before touching any type that loads MSBuild or NuGet assemblies.
    /// </summary>
    /// <param name="feature">The feature that needs the SDK, e.g. "NuGet package references".</param>
    public static void EnsureAvailable(string feature)
    {
        if (IsAvailable) return;

        throw new InvalidOperationException(
            CreateUnavailableMessage(feature, Environment.Version, GetInstalledSdkVersions())
        );
    }

    internal static string CreateUnavailableMessage(string feature, Version runtimeVersion, IReadOnlyCollection<string> installedSdkVersions)
    {
        var installed = installedSdkVersions.Count == 0
            ? "none"
            : string.Join(", ", installedSdkVersions);

        return $"{feature} require a .NET SDK compatible with the .NET {runtimeVersion} runtime that csharprepl is running on, but none was found "
            + $"(installed SDKs: {installed}). SDKs newer than the runtime, like preview SDKs for the next .NET version, can't be used. "
            + $"Install the .NET {runtimeVersion.Major} SDK from https://dotnet.microsoft.com/download to use this feature.";
    }

    /// <summary>
    /// Lists the SDK versions under the dotnet installation csharprepl is running from. Best effort: only used to
    /// make the error message more helpful.
    /// </summary>
    private static IReadOnlyCollection<string> GetInstalledSdkVersions()
    {
        try
        {
            // the runtime directory is like C:\Program Files\dotnet\shared\Microsoft.NETCore.App\11.0.0\
            var dotnetRoot = Path.GetFullPath(Path.Combine(RuntimeEnvironment.GetRuntimeDirectory(), "../../../"));
            var sdkDirectory = Path.Combine(dotnetRoot, "sdk");
            return Directory.Exists(sdkDirectory)
                ? Directory.GetDirectories(sdkDirectory)
                    .Select(Path.GetFileName)
                    .OfType<string>()
                    .Where(name => char.IsAsciiDigit(name.FirstOrDefault()))
                    .Order(StringComparer.OrdinalIgnoreCase)
                    .ToArray()
                : [];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }
}
