// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Loader;
using System.Threading;

namespace CSharpRepl.Services.Nuget;

/// <summary>
/// Makes native libraries that ship inside nuget packages (under <c>runtimes/&lt;rid&gt;/native/</c>)
/// loadable at runtime. Unlike a normal <c>dotnet build</c>, the REPL references package assemblies
/// in-place from the package cache and never copies their native assets next to an output executable,
/// so the default p/invoke search (next to the calling assembly + the OS search path) can't find them.
/// We register the directories that contain the current RID's native assets and resolve unmanaged dll
/// loads out of them as a fallback. https://github.com/waf/CSharpRepl/issues/375
/// </summary>
/// <remarks>
/// The p/invoking package assemblies (e.g. <c>SQLitePCLRaw.provider.e_sqlite3</c>) are loaded by Roslyn's
/// <c>InteractiveAssemblyLoader</c> into its own non-default <see cref="AssemblyLoadContext"/>, and
/// <see cref="AssemblyLoadContext.ResolvingUnmanagedDll"/> only fires on the context of the assembly that
/// declared the p/invoke. We therefore attach the resolver to every load context in the process (the
/// default one plus any that scripting creates), discovering new ones as assemblies are loaded.
/// </remarks>
internal static class NativeAssemblyResolver
{
    // The handler is attached to process-wide load contexts, so the set of search directories is
    // necessarily process-global as well.
    private static readonly ConcurrentDictionary<string, byte> SearchDirectories = new(StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<AssemblyLoadContext> HookedContexts = new();
    private static int initialized;

    /// <summary>
    /// Registers a directory (e.g. a package's <c>runtimes/&lt;rid&gt;/native</c> folder) to be searched
    /// when an unmanaged dll fails to load through the default mechanism.
    /// </summary>
    public static void AddSearchDirectory(string directory)
    {
        if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory))
        {
            return;
        }

        SearchDirectories.TryAdd(directory, 0);
        EnsureHooksInstalled();
    }

    private static void EnsureHooksInstalled()
    {
        if (Interlocked.Exchange(ref initialized, 1) != 0)
        {
            return;
        }

        // Hook the contexts that already exist, then keep hooking new ones as they appear. Scripting's
        // load context is typically created after this point (when the first submission is executed),
        // so the AssemblyLoad subscription is what actually catches it.
        AppDomain.CurrentDomain.AssemblyLoad += (_, e) => HookContextOf(e.LoadedAssembly);
        HookContext(AssemblyLoadContext.Default);
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            HookContextOf(assembly);
        }
    }

    private static void HookContextOf(Assembly assembly)
    {
        var context = AssemblyLoadContext.GetLoadContext(assembly);
        if (context is not null)
        {
            HookContext(context);
        }
    }

    private static void HookContext(AssemblyLoadContext context)
    {
        lock (HookedContexts)
        {
            if (HookedContexts.Add(context))
            {
                context.ResolvingUnmanagedDll += Resolve;
            }
        }
    }

    private static IntPtr Resolve(Assembly assembly, string unmanagedDllName)
    {
        foreach (var directory in SearchDirectories.Keys)
        {
            foreach (var candidate in GetCandidateFileNames(unmanagedDllName))
            {
                var path = Path.Combine(directory, candidate);
                if (File.Exists(path) && NativeLibrary.TryLoad(path, out var handle))
                {
                    return handle;
                }
            }
        }

        return IntPtr.Zero;
    }

    /// <summary>
    /// A p/invoke name like <c>e_sqlite3</c> maps to a platform-decorated file name on disk
    /// (<c>e_sqlite3.dll</c>, <c>libe_sqlite3.so</c>, <c>libe_sqlite3.dylib</c>). The name may also
    /// already be fully decorated, so we try it verbatim first.
    /// </summary>
    private static IEnumerable<string> GetCandidateFileNames(string unmanagedDllName)
    {
        yield return unmanagedDllName;

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            yield return unmanagedDllName + ".dll";
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            yield return "lib" + unmanagedDllName + ".dylib";
            yield return unmanagedDllName + ".dylib";
        }
        else
        {
            yield return "lib" + unmanagedDllName + ".so";
            yield return unmanagedDllName + ".so";
        }
    }
}
