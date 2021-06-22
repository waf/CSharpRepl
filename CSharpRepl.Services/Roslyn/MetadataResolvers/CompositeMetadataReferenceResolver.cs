// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Microsoft.CodeAnalysis;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace CSharpRepl.Services.Roslyn.MetadataResolvers
{
    /// <summary>
    /// A <see cref="MetadataReferenceResolver"/> that is contained by the <see cref="CompositeMetadataReferenceResolver"/>.
    /// It gets a chance to resolve a reference; if it doesn't, the next <see cref="IIndividualMetadataReferenceResolver"/> is called.
    /// </summary>
    internal interface IIndividualMetadataReferenceResolver
    {
        ImmutableArray<PortableExecutableReference> ResolveReference(string reference, string baseFilePath, MetadataReferenceProperties properties, MetadataReferenceResolver compositeResolver);
    }

    /// <summary>
    /// A top-level metadata resolver. We can only specify a single <see cref="MetadataReferenceResolver"/> in roslyn scripting.
    /// This composite class delegates to individual implementations (nuget resolver, assembly resolver, csproj resolver, etc).
    /// </summary>
    internal class CompositeMetadataReferenceResolver : MetadataReferenceResolver, IEquatable<CompositeMetadataReferenceResolver>
    {
        private readonly IIndividualMetadataReferenceResolver[] resolvers;

        public CompositeMetadataReferenceResolver(params IIndividualMetadataReferenceResolver[] resolvers) =>
            this.resolvers = resolvers;

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
            Equals(other as CompositeMetadataReferenceResolver);

        public bool Equals(CompositeMetadataReferenceResolver other) =>
            other != null
            && EqualityComparer<IIndividualMetadataReferenceResolver[]>.Default.Equals(resolvers, other.resolvers);

        public override int GetHashCode() =>
            HashCode.Combine(resolvers);
    }
}
