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
/// Makes native libraries that ship inside nuget packages loadable at runtime. Unlike a normal `dotnet build`, the REPL references package assemblies
/// in-place from the package cache and never copies their native assets next to an output executable, so the default p/invoke search (next to the calling
/// assembly + the OS search path) can't find them. https://github.com/waf/CSharpRepl/issues/375
/// </summary>
/// <remarks>
/// Owned by <see cref="Roslyn.References.AssemblyReferenceService"/> so native (GH Issue #375) and managed (GH Issue #355) resolution share one session-scoped
/// owner. The handler is attached to the load contexts in the process, so the set of search directories is effectivelyprocess-wide as well.
/// </remarks>
internal sealed class NativeAssemblyResolver
{
    private readonly ConcurrentDictionary<string, byte> searchDirectories = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<AssemblyLoadContext> hookedContexts = new();
    private int initialized;

    /// <summary>
    /// Registers a directory (e.g. a package's <c>runtimes/&lt;rid&gt;/native</c> folder) to be searched
    /// when an unmanaged dll fails to load through the default mechanism.
    /// </summary>
    public void AddSearchDirectory(string directory)
    {
        if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory))
        {
            return;
        }

        searchDirectories.TryAdd(directory, 0);
        EnsureHooksInstalled();
    }

    private void EnsureHooksInstalled()
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

    private void HookContextOf(Assembly assembly)
    {
        var context = AssemblyLoadContext.GetLoadContext(assembly);
        if (context is not null)
        {
            HookContext(context);
        }
    }

    private void HookContext(AssemblyLoadContext context)
    {
        lock (hookedContexts)
        {
            if (hookedContexts.Add(context))
            {
                context.ResolvingUnmanagedDll += Resolve;
            }
        }
    }

    private IntPtr Resolve(Assembly assembly, string unmanagedDllName)
    {
        foreach (var directory in searchDirectories.Keys)
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
