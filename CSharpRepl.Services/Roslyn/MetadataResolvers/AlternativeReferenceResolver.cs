// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Collections.Immutable;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;

namespace CSharpRepl.Services.Roslyn.MetadataResolvers;
/// <summary>
/// An alternative to MetadataReferenceResolver.ResolveReference. <br/>
/// This can be used when multiple references can be added from a single ResolveReference call, as Roslyn does not yet support it (https://github.com/dotnet/roslyn/issues/6900).
/// </summary>
public abstract class AlternativeReferenceResolver : IIndividualMetadataReferenceResolver
{
    public static PortableExecutableReference DummyReference { get; } = MetadataReference.CreateFromFile(typeof(object).Assembly.Location);

    public ImmutableArray<PortableExecutableReference> ResolveReference(string reference, string? baseFilePath, MetadataReferenceProperties properties, MetadataReferenceResolver compositeResolver)
    {
        if (CanResolve(reference))
            return ImmutableArray.Create(DummyReference);

        return ImmutableArray<PortableExecutableReference>.Empty;
    }

    public abstract bool CanResolve(string reference);

    public virtual Task<ImmutableArray<PortableExecutableReference>> ResolveAsync(string reference, CancellationToken cancellationToken)
        => Task.FromResult(Resolve(reference));

    public virtual ImmutableArray<PortableExecutableReference> Resolve(string reference)
        => ImmutableArray<PortableExecutableReference>.Empty;

}
