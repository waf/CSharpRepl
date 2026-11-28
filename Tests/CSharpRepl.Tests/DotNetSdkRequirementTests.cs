// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using CSharpRepl.Services.Roslyn.References;
using Xunit;

namespace CSharpRepl.Tests;

/// <summary>
/// https://github.com/waf/CSharpRepl/issues/526 - with only a preview SDK of the next major .NET version installed,
/// MSBuildLocator registers nothing, and NuGet references used to crash with a FileNotFoundException.
/// </summary>
public class DotNetSdkRequirementTests
{
    [Fact]
    public void CreateUnavailableMessage_OnlyNewerPreviewSdkInstalled_ExplainsWhichSdkToInstall()
    {
        var message = DotNetSdkRequirement.CreateUnavailableMessage(
            "NuGet package references",
            new Version(11, 0, 2),
            ["12.0.100-rc.1.27001.1"]);

        Assert.Equal(
            "NuGet package references require a .NET SDK compatible with the .NET 11.0.2 runtime that csharprepl is running on, but none was found "
            + "(installed SDKs: 12.0.100-rc.1.27001.1). SDKs newer than the runtime, like preview SDKs for the next .NET version, can't be used. "
            + "Install the .NET 11 SDK from https://dotnet.microsoft.com/download to use this feature.",
            message);
    }

    [Fact]
    public void CreateUnavailableMessage_NoSdksInstalled_SaysNone()
    {
        var message = DotNetSdkRequirement.CreateUnavailableMessage("Project and solution references", new Version(11, 0, 0), []);

        Assert.Contains("(installed SDKs: none)", message);
        Assert.Contains("Install the .NET 11 SDK", message);
    }

    [Fact]
    public void EnsureAvailable_SdkRegistered_DoesNotThrow()
    {
        // TestAssemblyInitializer registers MSBuildLocator for the whole test assembly.
        Assert.True(DotNetSdkRequirement.IsAvailable);
        DotNetSdkRequirement.EnsureAvailable("NuGet package references");
    }
}
