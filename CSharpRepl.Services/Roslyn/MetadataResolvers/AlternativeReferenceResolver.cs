using Microsoft.CodeAnalysis;
using System.Collections.Immutable;
using System.Threading;
using System.Threading.Tasks;

namespace CSharpRepl.Services.Roslyn.MetadataResolvers;
/// <summary>
/// An alternative to MetadataReferenceResolver.ResolveReference. <br/>
/// This can be used when multiple references can be added from a single ResolveReference call, as Roslyn does not yet support it (https://github.com/dotnet/roslyn/issues/6900).
/// </summary>
public abstract class AlternativeReferenceResolver : IIndividualMetadataReferenceResolver
{
    public ImmutableArray<PortableExecutableReference> ResolveReference(string reference, string? baseFilePath, MetadataReferenceProperties properties, MetadataReferenceResolver compositeResolver)
    {
        if (CanResolve(reference))
            return ImmutableArray.Create(DummyReference);

        return ImmutableArray<PortableExecutableReference>.Empty;
    }

    public abstract bool CanResolve(string reference);
    public virtual Task<ImmutableArray<PortableExecutableReference>> ResolveAsync(string reference, CancellationToken cancellationToken)
    {
        return Task.FromResult(Resolve(reference));
    }
    public virtual ImmutableArray<PortableExecutableReference> Resolve(string reference)
    {
        return ImmutableArray<PortableExecutableReference>.Empty;
    }
    public static PortableExecutableReference DummyReference { get; } = MetadataReference.CreateFromFile(typeof(object).Assembly.Location);
}
