// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Reflection.PortableExecutable;
using System.Threading.Tasks;
using ICSharpCode.Decompiler.Metadata;

namespace CSharpRepl.Services.CodeTransformation.Lowering;

/// <summary>
/// Resolves the assemblies referenced by the user's compiled code so ILSpy's CSharpDecompiler can build a full type system.
/// </summary>
internal sealed class ImplementationAssemblyResolver : IAssemblyResolver, IDisposable
{
    private readonly IReadOnlyCollection<string> searchDirectories;
    private readonly ConcurrentDictionary<string, MetadataFile?> cache = new(StringComparer.OrdinalIgnoreCase);

    public ImplementationAssemblyResolver(IReadOnlyCollection<string> searchDirectories)
    {
        this.searchDirectories = searchDirectories;
    }

    public MetadataFile? Resolve(IAssemblyReference reference) => Load(reference.Name + ".dll");

    public MetadataFile? ResolveModule(MetadataFile mainModule, string moduleName) => Load(moduleName);

    public Task<MetadataFile?> ResolveAsync(IAssemblyReference reference) => Task.FromResult(Resolve(reference));

    public Task<MetadataFile?> ResolveModuleAsync(MetadataFile mainModule, string moduleName) => Task.FromResult(ResolveModule(mainModule, moduleName));

    private MetadataFile? Load(string fileName)
    {
        foreach (var directory in searchDirectories)
        {
            var path = Path.Combine(directory, fileName);
            if (File.Exists(path))
            {
                return cache.GetOrAdd(path, static p =>
                {
                    try
                    {
                        return new PEFile(p, PEStreamOptions.PrefetchMetadata);
                    }
                    catch (Exception)
                    {
                        // unresolved references leave the decompiled type names unqualified, so a load failure is non-fatal: skip it and try the next.
                        return null;
                    }
                });
            }
        }
        return null;
    }

    public void Dispose()
    {
        foreach (var file in cache.Values)
        {
            file?.Dispose();
        }
        cache.Clear();
    }
}
