// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.Loader;
using System.Threading;

namespace CSharpRepl.Services.Roslyn.References;

/// <summary>
/// Attaches a per-context handler (e.g. <see cref="AssemblyLoadContext.Resolving"/> or
/// <see cref="AssemblyLoadContext.ResolvingUnmanagedDll"/>) to every load context in the process, including
/// ones created later. Used by both the managed (<see cref="ReplAssemblyLoader"/>, #355) and native
/// (<see cref="Nuget.NativeAssemblyResolver"/>, #375) resolve fallbacks: the context that needs the fallback -
/// notably Roslyn's submission context - is typically created after the fallback is set up, so hooking the
/// existing contexts isn't enough; the <see cref="AppDomain.AssemblyLoad"/> subscription catches the rest as
/// they appear.
/// </summary>
internal sealed class AssemblyLoadContextHook(Action<AssemblyLoadContext> attachHandler)
{
    private readonly Lock lockObject = new();
    private readonly HashSet<AssemblyLoadContext> hookedContexts = [];
    private int initialized;

    /// <summary>Hooks the contexts that already exist, then keeps hooking new ones as they appear.</summary>
    public void EnsureInstalled()
    {
        if (Interlocked.Exchange(ref initialized, 1) != 0)
        {
            return;
        }

        AppDomain.CurrentDomain.AssemblyLoad += (_, e) => HookContextOf(e.LoadedAssembly);
        Hook(AssemblyLoadContext.Default);
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            HookContextOf(assembly);
        }
    }

    private void HookContextOf(Assembly assembly)
    {
        var context = AssemblyLoadContext.GetLoadContext(assembly);
        if (context is not null)
        {
            Hook(context);
        }
    }

    private void Hook(AssemblyLoadContext context)
    {
        lock (lockObject)
        {
            if (hookedContexts.Add(context))
            {
                attachHandler(context);
            }
        }
    }
}
