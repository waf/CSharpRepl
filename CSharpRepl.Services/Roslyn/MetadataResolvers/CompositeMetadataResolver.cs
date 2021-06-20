using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Scripting;
using PrettyPrompt.Consoles;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;

namespace CSharpRepl.Services.Roslyn.MetadataResolvers
{
    /// <summary>
    /// A <see cref="MetadataReferenceResolver"/> that is contained by the <see cref="CompositeMetadataResolver"/>.
    /// It gets a chance to resolve a reference; if it doesn't, the next <see cref="IChildMetadataReferenceResolver"/> is called.
    /// </summary>
    public interface IChildMetadataReferenceResolver
    {
        ImmutableArray<PortableExecutableReference> ResolveReference(string reference, string? baseFilePath, MetadataReferenceProperties properties, MetadataReferenceResolver rootResolver);
    }

    class CompositeMetadataResolver : MetadataReferenceResolver, IEquatable<CompositeMetadataResolver>
    {
        private readonly IChildMetadataReferenceResolver[] resolvers;

        public CompositeMetadataResolver(params IChildMetadataReferenceResolver[] resolvers)
        {
            this.resolvers = resolvers;
        }

        public override ImmutableArray<PortableExecutableReference> ResolveReference(string reference, string baseFilePath, MetadataReferenceProperties properties)
        {
            reference = reference.Trim();

            foreach (var resolver in resolvers)
            {
                var resolved = resolver.ResolveReference(reference, baseFilePath, properties, this);
                if(resolved.Any())
                {
                    return resolved;
                }
            }

            return ImmutableArray<PortableExecutableReference>.Empty;
        }

        public override bool Equals(object other) =>
            Equals(other as CompositeMetadataResolver);

        public bool Equals(CompositeMetadataResolver other) =>
            other != null
            && EqualityComparer<IChildMetadataReferenceResolver[]>.Default.Equals(resolvers, other.resolvers);

        public override int GetHashCode() =>
            HashCode.Combine(resolvers);
    }
}
