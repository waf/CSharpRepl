// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Runtime.CompilerServices;
using Microsoft.Build.Locator;

namespace CSharpRepl.Tests;

internal static class TestAssemblyInitializer
{
    /// <summary>
    /// Registers MSBuildLocator once, as early as possible (on test-assembly load, before any test runs).
    /// <para>
    /// Many types here transitively depend on MSBuild / Roslyn-workspaces / NuGet assemblies, which resolve
    /// from the installed .NET SDK only after <see cref="MSBuildLocator.RegisterDefaults"/> has run. Previously
    /// that happened only as a side effect of the first <c>RoslynServices</c> initialization, so any test
    /// touching those assemblies (e.g. NuGet restore, the NuGet logger) had to run inside the
    /// <c>RoslynServices</c> collection / the full suite and would throw <c>FileNotFoundException</c> in
    /// isolation. Doing it in a module initializer satisfies the dependency process-wide, independent of which
    /// tests run or in what order, so such tests can also run on their own.
    /// </para>
    /// <para>
    /// This only registers MSBuildLocator; it does not serialize anything. Tests that need to run serially for
    /// other process-global reasons (the loader's <c>AssemblyLoadContext.Resolving</c> hooks) still use the
    /// <c>RoslynServices</c> collection. Production's own <c>RegisterDefaults</c> call discards its result and
    /// swallows the "already registered" exception, so pre-registering here changes no behavior.
    /// </para>
    /// </summary>
    [ModuleInitializer]
    internal static void RegisterMSBuild()
    {
        if (MSBuildLocator.IsRegistered) return;

        try
        {
            MSBuildLocator.RegisterDefaults();
        }
        catch (InvalidOperationException)
        {
            // No SDK discoverable, or a concurrent registration won the race. Tests that genuinely need
            // MSBuild surface a clear failure on use; tests that don't are unaffected. Never let a module
            // initializer throw - that would fault the entire test assembly.
        }
    }
}
