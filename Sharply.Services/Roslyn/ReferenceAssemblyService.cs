using Microsoft.CodeAnalysis;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;

namespace Sharply.Services.Roslyn
{
    /// <remarks>
    /// Useful notes https://github.com/dotnet/roslyn/blob/main/docs/wiki/Runtime-code-generation-using-Roslyn-compilations-in-.NET-Core-App.md
    /// </remarks>
    class ReferenceAssemblyService
    {
        private readonly IDictionary<string, MetadataReference> CachedMetadataReferences = new Dictionary<string, MetadataReference>();
        private readonly HashSet<MetadataReference> UniqueMetadataReferences = new HashSet<MetadataReference>(new EqualityComparerFunc<MetadataReference>(
            (r1, r2) => r1.Display.Equals(r2.Display),
            (r1) => r1.Display.GetHashCode()
        ));

        public string CurrentReferenceAssemblyPath { get; }
        public string CurrentImplementationAssemblyPath { get; }
        public IReadOnlyCollection<MetadataReference> DefaultImplementationAssemblies { get; }
        public IReadOnlyCollection<MetadataReference> DefaultReferenceAssemblies { get; }
        public IReadOnlyCollection<string> DefaultUsings { get; }

        public ReferenceAssemblyService()
        {
            this.CurrentReferenceAssemblyPath = GetCurrentAssemblyReferencePath();
            this.DefaultReferenceAssemblies = CreateDefaultReferences(CurrentReferenceAssemblyPath, DefaultReferenceAssemblyFilenames);

            this.CurrentImplementationAssemblyPath = Path.GetDirectoryName(typeof(object).Assembly.Location);
            this.DefaultImplementationAssemblies = CreateDefaultReferences(CurrentImplementationAssemblyPath, DefaultImplementationAssemblyFilenames(CurrentImplementationAssemblyPath));

            this.DefaultUsings = new[]
            {
                "System", "System.IO", "System.Collections.Generic",
                "System.Linq", "System.Net.Http",
                "System.Text", "System.Threading.Tasks"
            };
        }

        internal IReadOnlyCollection<MetadataReference> EnsureReferenceAssemblyWithDocumentation(IReadOnlyCollection<MetadataReference> references)
        {
            UniqueMetadataReferences.UnionWith(
                references.Select(suppliedReference => EnsureReferenceAssembly(suppliedReference)).Where(reference => reference is not null)
            );
            return UniqueMetadataReferences;
        }

        // a bit of a mismatch -- the scripting APIs use implementation assemblies only. The general workspace/project roslyn APIs use the reference
        // assemblies. Just to ensure that we don't accidentally use an implementation assembly with the roslyn APIs, do some "best-effort" conversion
        // here. If it's a reference assembly, pass it through unchanged. If it's an implementation assembly, try to locate the corresponding reference assembly.
        private MetadataReference EnsureReferenceAssembly(MetadataReference reference)
        {
            string suppliedAssemblyPath = reference.Display;
            if (CachedMetadataReferences.TryGetValue(suppliedAssemblyPath, out MetadataReference cachedReference))
            {
                return cachedReference;
            }

            // it's already a reference assembly, just cache it and use it.
            if(reference.Display.StartsWith(CurrentReferenceAssemblyPath))
            {
                CachedMetadataReferences[suppliedAssemblyPath] = reference;
                return reference;
            }

            // it's probably an implementation assembly, find the corresponding reference assembly and documentation if we can.

            var suppliedAssemblyName = Path.GetFileName(suppliedAssemblyPath);

            var potentialReferenceAssemblyPath = Path.Combine(CurrentReferenceAssemblyPath, suppliedAssemblyName);
            var assembly = File.Exists(potentialReferenceAssemblyPath)
                ? potentialReferenceAssemblyPath
                : suppliedAssemblyPath;

            if(assembly.StartsWith(CurrentImplementationAssemblyPath))
            {
                return null;
            }

            var potentialDocumentationPath = Path.ChangeExtension(assembly, ".xml");
            var documentation = File.Exists(potentialDocumentationPath)
                ? XmlDocumentationProvider.CreateFromFile(potentialDocumentationPath)
                : null;

            var completeMetadataReference = MetadataReference.CreateFromFile(assembly, documentation: documentation);
            CachedMetadataReferences[suppliedAssemblyPath] = completeMetadataReference;
            return completeMetadataReference;
        }

        private static string GetCurrentAssemblyReferencePath()
        {
            var version = Environment.Version;
            var dotnetRuntimePath = RuntimeEnvironment.GetRuntimeDirectory();
            var dotnetRoot = Path.Combine(dotnetRuntimePath, "../../../packs/Microsoft.NETCore.App.Ref");
            var referenceAssemblyPath = Directory
                .GetDirectories(dotnetRoot, "*net" + version.Major.ToString() + "*", SearchOption.AllDirectories)
                .Last();
            return Path.GetFullPath(referenceAssemblyPath);
        }

        private IReadOnlyCollection<MetadataReference> CreateDefaultReferences(string assemblyPath, IReadOnlyCollection<string> assemblies)
        {
            return assemblies
                .Select(dll =>
                {
                    var fullReferencePath = Path.Combine(assemblyPath, dll);
                    var fullDocumentationPath = Path.ChangeExtension(fullReferencePath, ".xml");

                    if (!File.Exists(fullReferencePath))
                        return null;
                    if (!File.Exists(fullDocumentationPath))
                        return MetadataReference.CreateFromFile(fullReferencePath);

                    return MetadataReference.CreateFromFile(fullReferencePath, documentation: XmlDocumentationProvider.CreateFromFile(fullDocumentationPath));
                })
                .Where(reference => reference is not null)
                .ToList();
        }

        // this list matches the default reference assemblies when you create a new .NET 5 console app in Visual Studio.
        private string[] DefaultReferenceAssemblyFilenames =>
            new[]
            {
                "Microsoft.CSharp.dll", "Microsoft.VisualBasic.Core.dll", "Microsoft.VisualBasic.dll", "Microsoft.Win32.Primitives.dll", "mscorlib.dll", "netstandard.dll", "System.AppContext.dll", "System.Buffers.dll", "System.Collections.Concurrent.dll", "System.Collections.dll", "System.Collections.Immutable.dll", "System.Collections.NonGeneric.dll", "System.Collections.Specialized.dll",
                "System.ComponentModel.Annotations.dll", "System.ComponentModel.DataAnnotations.dll", "System.ComponentModel.dll", "System.ComponentModel.EventBasedAsync.dll", "System.ComponentModel.Primitives.dll", "System.ComponentModel.TypeConverter.dll", "System.Configuration.dll", "System.Console.dll", "System.Core.dll", "System.Data.Common.dll", "System.Data.DataSetExtensions.dll", "System.Data.dll",
                "System.Diagnostics.Contracts.dll", "System.Diagnostics.Debug.dll", "System.Diagnostics.DiagnosticSource.dll", "System.Diagnostics.FileVersionInfo.dll", "System.Diagnostics.Process.dll", "System.Diagnostics.StackTrace.dll", "System.Diagnostics.TextWriterTraceListener.dll", "System.Diagnostics.Tools.dll", "System.Diagnostics.TraceSource.dll", "System.Diagnostics.Tracing.dll",
                "System.dll", "System.Drawing.dll", "System.Drawing.Primitives.dll", "System.Dynamic.Runtime.dll", "System.Formats.Asn1.dll", "System.Globalization.Calendars.dll", "System.Globalization.dll", "System.Globalization.Extensions.dll", "System.IO.Compression.Brotli.dll", "System.IO.Compression.dll", "System.IO.Compression.FileSystem.dll", "System.IO.Compression.ZipFile.dll", "System.IO.dll",
                "System.IO.FileSystem.dll", "System.IO.FileSystem.DriveInfo.dll", "System.IO.FileSystem.Primitives.dll", "System.IO.FileSystem.Watcher.dll", "System.IO.IsolatedStorage.dll", "System.IO.MemoryMappedFiles.dll", "System.IO.Pipes.dll", "System.IO.UnmanagedMemoryStream.dll", "System.Linq.dll", "System.Linq.Expressions.dll", "System.Linq.Parallel.dll", "System.Linq.Queryable.dll",
                "System.Memory.dll", "System.Net.dll", "System.Net.Http.dll", "System.Net.Http.Json.dll", "System.Net.HttpListener.dll", "System.Net.Mail.dll", "System.Net.NameResolution.dll", "System.Net.NetworkInformation.dll", "System.Net.Ping.dll", "System.Net.Primitives.dll", "System.Net.Requests.dll", "System.Net.Security.dll", "System.Net.ServicePoint.dll", "System.Net.Sockets.dll",
                "System.Net.WebClient.dll", "System.Net.WebHeaderCollection.dll", "System.Net.WebProxy.dll", "System.Net.WebSockets.Client.dll", "System.Net.WebSockets.dll", "System.Numerics.dll", "System.Numerics.Vectors.dll", "System.ObjectModel.dll", "System.Reflection.DispatchProxy.dll", "System.Reflection.dll", "System.Reflection.Emit.dll", "System.Reflection.Emit.ILGeneration.dll",
                "System.Reflection.Emit.Lightweight.dll", "System.Reflection.Extensions.dll", "System.Reflection.Metadata.dll", "System.Reflection.Primitives.dll", "System.Reflection.TypeExtensions.dll", "System.Resources.Reader.dll", "System.Resources.ResourceManager.dll", "System.Resources.Writer.dll", "System.Runtime.CompilerServices.Unsafe.dll", "System.Runtime.CompilerServices.VisualC.dll",
                "System.Runtime.dll", "System.Runtime.Extensions.dll", "System.Runtime.Handles.dll", "System.Runtime.InteropServices.dll", "System.Runtime.InteropServices.RuntimeInformation.dll", "System.Runtime.Intrinsics.dll", "System.Runtime.Loader.dll", "System.Runtime.Numerics.dll", "System.Runtime.Serialization.dll", "System.Runtime.Serialization.Formatters.dll",
                "System.Runtime.Serialization.Json.dll", "System.Runtime.Serialization.Primitives.dll", "System.Runtime.Serialization.Xml.dll", "System.Security.Claims.dll", "System.Security.Cryptography.Algorithms.dll", "System.Security.Cryptography.Csp.dll", "System.Security.Cryptography.Encoding.dll", "System.Security.Cryptography.Primitives.dll", "System.Security.Cryptography.X509Certificates.dll",
                "System.Security.dll", "System.Security.Principal.dll", "System.Security.SecureString.dll", "System.ServiceModel.Web.dll", "System.ServiceProcess.dll", "System.Text.Encoding.CodePages.dll", "System.Text.Encoding.dll", "System.Text.Encoding.Extensions.dll", "System.Text.Encodings.Web.dll", "System.Text.Json.dll", "System.Text.RegularExpressions.dll", "System.Threading.Channels.dll",
                "System.Threading.dll", "System.Threading.Overlapped.dll", "System.Threading.Tasks.Dataflow.dll", "System.Threading.Tasks.dll", "System.Threading.Tasks.Extensions.dll", "System.Threading.Tasks.Parallel.dll", "System.Threading.Thread.dll", "System.Threading.ThreadPool.dll", "System.Threading.Timer.dll", "System.Transactions.dll", "System.Transactions.Local.dll", "System.ValueTuple.dll",
                "System.Web.dll", "System.Web.HttpUtility.dll", "System.Windows.dll", "System.Xml.dll", "System.Xml.Linq.dll", "System.Xml.ReaderWriter.dll", "System.Xml.Serialization.dll", "System.Xml.XDocument.dll", "System.Xml.XmlDocument.dll", "System.Xml.XmlSerializer.dll", "System.Xml.XPath.dll", "System.Xml.XPath.XDocument.dll", "WindowsBase.dll"
            };

        private static string[] DefaultImplementationAssemblyFilenames(string prefix) =>
            AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES").ToString()
                .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(name => name.StartsWith(prefix))
                .ToArray();
            
    }
}
