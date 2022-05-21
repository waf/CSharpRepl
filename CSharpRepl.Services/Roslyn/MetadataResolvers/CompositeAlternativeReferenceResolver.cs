// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Microsoft.CodeAnalysis;
using System;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace CSharpRepl.Services.Roslyn.MetadataResolvers;
internal sealed class CompositeAlternativeReferenceResolver
{
    private readonly AlternativeReferenceResolver[] alternativeResolvers;

    public CompositeAlternativeReferenceResolver(params AlternativeReferenceResolver[] alternativeResolvers)
    {
        this.alternativeResolvers = alternativeResolvers;
    }

    public async Task<ImmutableArray<PortableExecutableReference>> GetAllAlternativeReferences(string code, CancellationToken cancellationToken)
    {
        var splitCommands = code.Split(new[] { '\r', '\n' }, StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

        var resolveReferencesTasks =
            from cmd in splitCommands
            from resolver in alternativeResolvers
            where resolver.CanResolve(cmd)
            select resolver.ResolveAsync(cmd, cancellationToken);

        return (await Task.WhenAll(resolveReferencesTasks))
            .SelectMany(r => r)
            .ToImmutableArray();
    }
}
