// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Microsoft.SymbolStore;
using Microsoft.SymbolStore.KeyGenerators;
using Microsoft.SymbolStore.SymbolStores;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection.Metadata;
using System.Threading;
using System.Threading.Tasks;

namespace CSharpRepl.Services.SymbolExploration
{
    /// <summary>
    /// Downloads PDB files from symbol servers for a given assembly file,
    /// and provides a <see cref="MetadataReader"/> for navigating them.
    /// </summary>
    /// <remarks>
    /// This code has been adapted from https://github.com/dotnet/symstore/tree/main/src/dotnet-symbol
    /// </remarks>
    internal sealed class DebugSymbolLoader : IDisposable
    {
        private readonly NullLogger logger;
        private readonly CacheSymbolStore symbolStore;
        private readonly SymbolStoreFile symbolStoreFile;

        private bool disposedValue;

        public DebugSymbolLoader(string assemblyFilePath)
        {
            this.logger = new NullLogger();
            this.symbolStore = BuildSymbolStore();
            this.symbolStoreFile = new SymbolStoreFile(
                File.Open(assemblyFilePath, FileMode.Open, FileAccess.Read, FileShare.Read),
                assemblyFilePath
            );
        }

        /// <summary>
        /// Creates our configuration of symbol stores (either remote or a local cache).
        /// Symbol stores are chained, and if a given store does not contain the requested symbol
        /// the next one in the chain will be called. If a symbol store contains the requested symbol,
        /// previous symbols in the chain will have a chance to add them (this is how caching works).
        /// </summary>
        private CacheSymbolStore BuildSymbolStore()
        {
            SymbolStore? store = null;

            foreach (var server in Configuration.SymbolServers)
            {
                store = new HttpSymbolStore(logger, store, new Uri(server), null);
            }

            var cacheDirectory = Path.Combine(Configuration.ApplicationDirectory, "symbols");
            return new CacheSymbolStore(logger, store, cacheDirectory);
        }

        /// <summary>
        /// Calculate assembly key (i.e. an identifier) for the assembly. This key
        /// is used to query the symbol stores.
        /// </summary>
        public IEnumerable<SymbolStoreKey> GetSymbolFileNames() =>
            new FileKeyGenerator(logger, symbolStoreFile)
                .GetKeys(KeyTypeFlags.SymbolKey)
                .ToList();

        public async Task<SymbolStoreFile> DownloadSymbolFile(SymbolStoreKey key, CancellationToken cancellationToken) =>
            await symbolStore.GetFile(key, cancellationToken);

        /// <summary>
        /// Produce a metadata reader we can use to navigate the PDB.
        /// </summary>
        public MetadataReader? ReadPortablePdb(SymbolStoreFile symbolFile)
        {
            if (symbolFile is null
                || !TryOpenPortablePdb(symbolFile, out var metadataReader)
                || metadataReader is null)
            {
                return null;
            }

            return metadataReader;
        }

        private static bool TryOpenPortablePdb(SymbolStoreFile symbolFile, out MetadataReader? mr)
        {
            try
            {
                var pdb = MetadataReaderProvider.FromPortablePdbStream(symbolFile.Stream, MetadataStreamOptions.Default);
                mr = pdb.GetMetadataReader();
                return true;
            }
            catch (BadImageFormatException) // can happen if a Windows PDB is returned
            {
                mr = null;
                return false;
            }
        }

        public void Dispose()
        {
            // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }

        private void Dispose(bool disposing)
        {
            if (!disposedValue)
            {
                if (disposing)
                {
                    this.symbolStore.Dispose();
                    this.symbolStoreFile.Dispose();
                }
                disposedValue = true;
            }
        }
    }
}
