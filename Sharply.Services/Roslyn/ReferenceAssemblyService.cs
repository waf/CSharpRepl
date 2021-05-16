using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
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

        public IReadOnlyCollection<string> ReferenceAssemblyPaths { get; }
        public IReadOnlyCollection<string> ImplementationAssemblyPaths { get; }
        public IReadOnlyCollection<MetadataReference> DefaultImplementationAssemblies { get; }
        public IReadOnlyCollection<MetadataReference> DefaultReferenceAssemblies { get; }
        public IReadOnlyCollection<string> DefaultUsings { get; }

        public ReferenceAssemblyService(Configuration config)
        {
            var sdks = GetSdkConfiguration(config.Sdk);

            this.ReferenceAssemblyPaths = sdks.Select(sdk => sdk.ReferencePath).ToArray();
            this.ImplementationAssemblyPaths = sdks.Select(sdk => sdk.ImplementationPath).ToArray();

            this.DefaultReferenceAssemblies = sdks
                .SelectMany(sdk => CreateDefaultReferences(
                    sdk.ReferencePath,
                    sdk.ReferenceAssemblies
                ))
                .ToList();

            this.DefaultImplementationAssemblies = sdks
                .SelectMany(sdk => CreateDefaultReferences(
                    sdk.ImplementationPath,
                    sdk.ImplementationAssemblies
                ))
                .Concat(CreateDefaultReferences("", config.References))
                .ToList();

            this.DefaultUsings = new[] {
                    "System", "System.IO", "System.Collections.Generic",
                    "System.Linq", "System.Net.Http",
                    "System.Text", "System.Threading.Tasks"
                }
                .Concat(config.Usings)
                .ToList();
        }

        private static string GetCurrentAssemblyImplementationPath(string sdk)
        {
            return Path
                .GetDirectoryName(typeof(object).Assembly.Location)
                .Replace(Sdk.NetCoreApp, sdk);
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
            if (ReferenceAssemblyPaths.Any(path => reference.Display.StartsWith(path)))
            {
                CachedMetadataReferences[suppliedAssemblyPath] = reference;
                return reference;
            }

            // it's probably an implementation assembly, find the corresponding reference assembly and documentation if we can.

            var suppliedAssemblyName = Path.GetFileName(suppliedAssemblyPath);

            var assembly = ReferenceAssemblyPaths
                .Select(path => Path.Combine(path, suppliedAssemblyName))
                .FirstOrDefault(potentialReferencePath => File.Exists(potentialReferencePath))
                ?? suppliedAssemblyPath;

            if (ImplementationAssemblyPaths.Any(path => assembly.StartsWith(path)))
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

        private Sdk[] GetSdkConfiguration(string sdk)
        {
            var referencePath = GetCurrentAssemblyReferencePath(sdk);
            var implementationPath = GetCurrentAssemblyImplementationPath(sdk);
            return sdk switch
            {
                Sdk.NetCoreApp => new[] {
                    new Sdk(sdk,
                        referencePath,
                        implementationPath,
                        NetCoreReferenceAssemblyFilenames,
                        NetCoreImplementationAssemblyFilepaths(implementationPath)
                    )
                },
                Sdk.AspNetCoreApp =>
                    GetSdkConfiguration(Sdk.NetCoreApp).Append(
                        new Sdk(sdk,
                            referencePath,
                            implementationPath,
                            AspNetCoreReferenceAssemblyFilenames,
                            AspNetCoreImplementationAssemblyFilepaths(implementationPath)
                         )
                    ).ToArray(),
                _ => throw new InvalidOperationException("Unknown SDK: " + sdk)
            };
        }

        private static string GetCurrentAssemblyReferencePath(string sdk)
        {
            var version = Environment.Version;
            var dotnetRuntimePath = RuntimeEnvironment.GetRuntimeDirectory();
            var dotnetRoot = Path.Combine(dotnetRuntimePath, "../../../packs/", sdk + ".Ref");
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
        private string[] NetCoreReferenceAssemblyFilenames =>
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

        private string[] AspNetCoreReferenceAssemblyFilenames =>
            new[]
            {
                "Microsoft.AspNetCore.Antiforgery.dll", "Microsoft.AspNetCore.Authentication.Abstractions.dll", "Microsoft.AspNetCore.Authentication.Cookies.dll", "Microsoft.AspNetCore.Authentication.Core.dll", "Microsoft.AspNetCore.Authentication.OAuth.dll", "Microsoft.AspNetCore.Authentication.dll", "Microsoft.AspNetCore.Authorization.Policy.dll", "Microsoft.AspNetCore.Authorization.dll",
                "Microsoft.AspNetCore.Components.Authorization.dll", "Microsoft.AspNetCore.Components.Forms.dll", "Microsoft.AspNetCore.Components.Server.dll", "Microsoft.AspNetCore.Components.Web.dll", "Microsoft.AspNetCore.Components.dll", "Microsoft.AspNetCore.Connections.Abstractions.dll", "Microsoft.AspNetCore.CookiePolicy.dll", "Microsoft.AspNetCore.Cors.dll", "Microsoft.AspNetCore.Cryptography.Internal.dll", "Microsoft.AspNetCore.Cryptography.KeyDerivation.dll",
                "Microsoft.AspNetCore.DataProtection.Abstractions.dll", "Microsoft.AspNetCore.DataProtection.Extensions.dll", "Microsoft.AspNetCore.DataProtection.dll", "Microsoft.AspNetCore.Diagnostics.Abstractions.dll", "Microsoft.AspNetCore.Diagnostics.HealthChecks.dll", "Microsoft.AspNetCore.Diagnostics.dll", "Microsoft.AspNetCore.HostFiltering.dll", "Microsoft.AspNetCore.Hosting.Abstractions.dll", "Microsoft.AspNetCore.Hosting.Server.Abstractions.dll",
                "Microsoft.AspNetCore.Hosting.dll", "Microsoft.AspNetCore.Html.Abstractions.dll", "Microsoft.AspNetCore.Http.Abstractions.dll", "Microsoft.AspNetCore.Http.Connections.Common.dll", "Microsoft.AspNetCore.Http.Connections.dll", "Microsoft.AspNetCore.Http.Extensions.dll", "Microsoft.AspNetCore.Http.Features.dll", "Microsoft.AspNetCore.Http.dll", "Microsoft.AspNetCore.HttpOverrides.dll", "Microsoft.AspNetCore.HttpsPolicy.dll",
                "Microsoft.AspNetCore.Identity.dll", "Microsoft.AspNetCore.Localization.Routing.dll", "Microsoft.AspNetCore.Localization.dll", "Microsoft.AspNetCore.Metadata.dll", "Microsoft.AspNetCore.Mvc.Abstractions.dll", "Microsoft.AspNetCore.Mvc.ApiExplorer.dll", "Microsoft.AspNetCore.Mvc.Core.dll", "Microsoft.AspNetCore.Mvc.Cors.dll", "Microsoft.AspNetCore.Mvc.DataAnnotations.dll", "Microsoft.AspNetCore.Mvc.Formatters.Json.dll", "Microsoft.AspNetCore.Mvc.Formatters.Xml.dll",
                "Microsoft.AspNetCore.Mvc.Localization.dll", "Microsoft.AspNetCore.Mvc.Razor.dll", "Microsoft.AspNetCore.Mvc.RazorPages.dll", "Microsoft.AspNetCore.Mvc.TagHelpers.dll", "Microsoft.AspNetCore.Mvc.ViewFeatures.dll", "Microsoft.AspNetCore.Mvc.dll", "Microsoft.AspNetCore.Razor.Runtime.dll", "Microsoft.AspNetCore.Razor.dll", "Microsoft.AspNetCore.ResponseCaching.Abstractions.dll", "Microsoft.AspNetCore.ResponseCaching.dll", "Microsoft.AspNetCore.ResponseCompression.dll",
                "Microsoft.AspNetCore.Rewrite.dll", "Microsoft.AspNetCore.Routing.Abstractions.dll", "Microsoft.AspNetCore.Routing.dll", "Microsoft.AspNetCore.Server.HttpSys.dll", "Microsoft.AspNetCore.Server.IIS.dll", "Microsoft.AspNetCore.Server.IISIntegration.dll", "Microsoft.AspNetCore.Server.Kestrel.Core.dll", "Microsoft.AspNetCore.Server.Kestrel.Transport.Sockets.dll", "Microsoft.AspNetCore.Server.Kestrel.dll", "Microsoft.AspNetCore.Session.dll",
                "Microsoft.AspNetCore.SignalR.Common.dll", "Microsoft.AspNetCore.SignalR.Core.dll", "Microsoft.AspNetCore.SignalR.Protocols.Json.dll", "Microsoft.AspNetCore.SignalR.dll", "Microsoft.AspNetCore.StaticFiles.dll", "Microsoft.AspNetCore.WebSockets.dll", "Microsoft.AspNetCore.WebUtilities.dll", "Microsoft.AspNetCore.dll", "Microsoft.Extensions.Caching.Abstractions.dll", "Microsoft.Extensions.Caching.Memory.dll",
                "Microsoft.Extensions.Configuration.Abstractions.dll", "Microsoft.Extensions.Configuration.Binder.dll", "Microsoft.Extensions.Configuration.CommandLine.dll", "Microsoft.Extensions.Configuration.EnvironmentVariables.dll", "Microsoft.Extensions.Configuration.FileExtensions.dll", "Microsoft.Extensions.Configuration.Ini.dll", "Microsoft.Extensions.Configuration.Json.dll", "Microsoft.Extensions.Configuration.KeyPerFile.dll",
                "Microsoft.Extensions.Configuration.UserSecrets.dll", "Microsoft.Extensions.Configuration.Xml.dll", "Microsoft.Extensions.Configuration.dll", "Microsoft.Extensions.DependencyInjection.Abstractions.dll", "Microsoft.Extensions.DependencyInjection.dll", "Microsoft.Extensions.Diagnostics.HealthChecks.Abstractions.dll", "Microsoft.Extensions.Diagnostics.HealthChecks.dll", "Microsoft.Extensions.FileProviders.Abstractions.dll", "Microsoft.Extensions.FileProviders.Composite.dll",
                "Microsoft.Extensions.FileProviders.Embedded.dll", "Microsoft.Extensions.FileProviders.Physical.dll", "Microsoft.Extensions.FileSystemGlobbing.dll", "Microsoft.Extensions.Hosting.Abstractions.dll", "Microsoft.Extensions.Hosting.dll", "Microsoft.Extensions.Http.dll", "Microsoft.Extensions.Identity.Core.dll", "Microsoft.Extensions.Identity.Stores.dll", "Microsoft.Extensions.Localization.Abstractions.dll", "Microsoft.Extensions.Localization.dll",
                "Microsoft.Extensions.Logging.Abstractions.dll", "Microsoft.Extensions.Logging.Configuration.dll", "Microsoft.Extensions.Logging.Console.dll", "Microsoft.Extensions.Logging.Debug.dll", "Microsoft.Extensions.Logging.EventLog.dll", "Microsoft.Extensions.Logging.EventSource.dll", "Microsoft.Extensions.Logging.TraceSource.dll", "Microsoft.Extensions.Logging.dll", "Microsoft.Extensions.ObjectPool.dll", "Microsoft.Extensions.Options.ConfigurationExtensions.dll",
                "Microsoft.Extensions.Options.DataAnnotations.dll", "Microsoft.Extensions.Options.dll", "Microsoft.Extensions.Primitives.dll", "Microsoft.Extensions.WebEncoders.dll", "Microsoft.JSInterop.dll", "Microsoft.Net.Http.Headers.dll", "Microsoft.Win32.Registry.dll", "System.Diagnostics.EventLog.dll", "System.IO.Pipelines.dll", "System.Security.AccessControl.dll", "System.Security.Cryptography.Cng.dll", "System.Security.Cryptography.Xml.dll", "System.Security.Permissions.dll",
                "System.Security.Principal.Windows.dll", "System.Windows.Extensions.dll",
            };

        private static string[] NetCoreImplementationAssemblyFilepaths(string prefix) =>
            AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES").ToString()
                .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(name => name.StartsWith(prefix))
                .ToArray();

        private string[] AspNetCoreImplementationAssemblyFilepaths(string prefix) =>
            new[]
            {
                "Microsoft.AspNetCore.Antiforgery.dll", "Microsoft.AspNetCore.Authentication.Abstractions.dll", "Microsoft.AspNetCore.Authentication.Cookies.dll", "Microsoft.AspNetCore.Authentication.Core.dll", "Microsoft.AspNetCore.Authentication.OAuth.dll", "Microsoft.AspNetCore.Authentication.dll", "Microsoft.AspNetCore.Authorization.Policy.dll", "Microsoft.AspNetCore.Authorization.dll",
                "Microsoft.AspNetCore.Components.Authorization.dll", "Microsoft.AspNetCore.Components.Forms.dll", "Microsoft.AspNetCore.Components.Server.dll", "Microsoft.AspNetCore.Components.Web.dll", "Microsoft.AspNetCore.Components.dll", "Microsoft.AspNetCore.Connections.Abstractions.dll", "Microsoft.AspNetCore.CookiePolicy.dll", "Microsoft.AspNetCore.Cors.dll",
                "Microsoft.AspNetCore.Cryptography.Internal.dll", "Microsoft.AspNetCore.Cryptography.KeyDerivation.dll", "Microsoft.AspNetCore.DataProtection.Abstractions.dll", "Microsoft.AspNetCore.DataProtection.Extensions.dll", "Microsoft.AspNetCore.DataProtection.dll", "Microsoft.AspNetCore.Diagnostics.Abstractions.dll", "Microsoft.AspNetCore.Diagnostics.HealthChecks.dll", "Microsoft.AspNetCore.Diagnostics.dll",
                "Microsoft.AspNetCore.HostFiltering.dll", "Microsoft.AspNetCore.Hosting.Abstractions.dll", "Microsoft.AspNetCore.Hosting.Server.Abstractions.dll", "Microsoft.AspNetCore.Hosting.dll", "Microsoft.AspNetCore.Html.Abstractions.dll", "Microsoft.AspNetCore.Http.Abstractions.dll", "Microsoft.AspNetCore.Http.Connections.Common.dll", "Microsoft.AspNetCore.Http.Connections.dll",
                "Microsoft.AspNetCore.Http.Extensions.dll", "Microsoft.AspNetCore.Http.Features.dll", "Microsoft.AspNetCore.Http.dll", "Microsoft.AspNetCore.HttpOverrides.dll", "Microsoft.AspNetCore.HttpsPolicy.dll", "Microsoft.AspNetCore.Identity.dll", "Microsoft.AspNetCore.Localization.Routing.dll", "Microsoft.AspNetCore.Localization.dll",
                "Microsoft.AspNetCore.Metadata.dll", "Microsoft.AspNetCore.Mvc.Abstractions.dll", "Microsoft.AspNetCore.Mvc.ApiExplorer.dll", "Microsoft.AspNetCore.Mvc.Core.dll", "Microsoft.AspNetCore.Mvc.Cors.dll", "Microsoft.AspNetCore.Mvc.DataAnnotations.dll", "Microsoft.AspNetCore.Mvc.Formatters.Json.dll", "Microsoft.AspNetCore.Mvc.Formatters.Xml.dll",
                "Microsoft.AspNetCore.Mvc.Localization.dll", "Microsoft.AspNetCore.Mvc.Razor.dll", "Microsoft.AspNetCore.Mvc.RazorPages.dll", "Microsoft.AspNetCore.Mvc.TagHelpers.dll", "Microsoft.AspNetCore.Mvc.ViewFeatures.dll", "Microsoft.AspNetCore.Mvc.dll", "Microsoft.AspNetCore.Razor.Runtime.dll", "Microsoft.AspNetCore.Razor.dll",
                "Microsoft.AspNetCore.ResponseCaching.Abstractions.dll", "Microsoft.AspNetCore.ResponseCaching.dll", "Microsoft.AspNetCore.ResponseCompression.dll", "Microsoft.AspNetCore.Rewrite.dll", "Microsoft.AspNetCore.Routing.Abstractions.dll", "Microsoft.AspNetCore.Routing.dll", "Microsoft.AspNetCore.Server.HttpSys.dll", "Microsoft.AspNetCore.Server.IIS.dll",
                "Microsoft.AspNetCore.Server.IISIntegration.dll", "Microsoft.AspNetCore.Server.Kestrel.Core.dll", "Microsoft.AspNetCore.Server.Kestrel.Transport.Sockets.dll", "Microsoft.AspNetCore.Server.Kestrel.dll", "Microsoft.AspNetCore.Session.dll", "Microsoft.AspNetCore.SignalR.Common.dll", "Microsoft.AspNetCore.SignalR.Core.dll", "Microsoft.AspNetCore.SignalR.Protocols.Json.dll",
                "Microsoft.AspNetCore.SignalR.dll", "Microsoft.AspNetCore.StaticFiles.dll", "Microsoft.AspNetCore.WebSockets.dll", "Microsoft.AspNetCore.WebUtilities.dll", "Microsoft.AspNetCore.dll", "Microsoft.Extensions.Caching.Abstractions.dll", "Microsoft.Extensions.Caching.Memory.dll", "Microsoft.Extensions.Configuration.Abstractions.dll",
                "Microsoft.Extensions.Configuration.Binder.dll", "Microsoft.Extensions.Configuration.CommandLine.dll", "Microsoft.Extensions.Configuration.EnvironmentVariables.dll", "Microsoft.Extensions.Configuration.FileExtensions.dll", "Microsoft.Extensions.Configuration.Ini.dll", "Microsoft.Extensions.Configuration.Json.dll", "Microsoft.Extensions.Configuration.KeyPerFile.dll", "Microsoft.Extensions.Configuration.UserSecrets.dll",
                "Microsoft.Extensions.Configuration.Xml.dll", "Microsoft.Extensions.Configuration.dll", "Microsoft.Extensions.DependencyInjection.Abstractions.dll", "Microsoft.Extensions.DependencyInjection.dll", "Microsoft.Extensions.Diagnostics.HealthChecks.Abstractions.dll", "Microsoft.Extensions.Diagnostics.HealthChecks.dll", "Microsoft.Extensions.FileProviders.Abstractions.dll", "Microsoft.Extensions.FileProviders.Composite.dll",
                "Microsoft.Extensions.FileProviders.Embedded.dll", "Microsoft.Extensions.FileProviders.Physical.dll", "Microsoft.Extensions.FileSystemGlobbing.dll", "Microsoft.Extensions.Hosting.Abstractions.dll", "Microsoft.Extensions.Hosting.dll", "Microsoft.Extensions.Http.dll", "Microsoft.Extensions.Identity.Core.dll", "Microsoft.Extensions.Identity.Stores.dll",
                "Microsoft.Extensions.Localization.Abstractions.dll", "Microsoft.Extensions.Localization.dll", "Microsoft.Extensions.Logging.Abstractions.dll", "Microsoft.Extensions.Logging.Configuration.dll", "Microsoft.Extensions.Logging.Console.dll", "Microsoft.Extensions.Logging.Debug.dll", "Microsoft.Extensions.Logging.EventLog.dll", "Microsoft.Extensions.Logging.EventSource.dll",
                "Microsoft.Extensions.Logging.TraceSource.dll", "Microsoft.Extensions.Logging.dll", "Microsoft.Extensions.ObjectPool.dll", "Microsoft.Extensions.Options.ConfigurationExtensions.dll", "Microsoft.Extensions.Options.DataAnnotations.dll", "Microsoft.Extensions.Options.dll", "Microsoft.Extensions.Primitives.dll", "Microsoft.Extensions.WebEncoders.dll",
                "Microsoft.JSInterop.dll", "Microsoft.Net.Http.Headers.dll", "Microsoft.Win32.SystemEvents.dll", "System.Diagnostics.EventLog.Messages.dll", "System.Diagnostics.EventLog.dll", "System.Drawing.Common.dll", "System.IO.Pipelines.dll", "System.Security.Cryptography.Pkcs.dll",
                "System.Security.Cryptography.Xml.dll", "System.Security.Permissions.dll", "System.Windows.Extensions.dll"
            }
            .Select(filename => Path.Combine(prefix, filename))
            .ToArray();
    }

    public record Sdk(string Name, string ReferencePath, string ImplementationPath, string[] ReferenceAssemblies, string[] ImplementationAssemblies)
    {
        public const string NetCoreApp = "Microsoft.NETCore.App";
        public const string AspNetCoreApp = "Microsoft.AspNetCore.App";

        public static IReadOnlyCollection<string> SupportedSdks { get; } = new[] { NetCoreApp, AspNetCoreApp };
    }
}
