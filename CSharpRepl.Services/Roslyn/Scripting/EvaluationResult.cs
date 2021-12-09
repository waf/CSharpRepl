// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Microsoft.CodeAnalysis;
using System;
using System.Collections.Generic;

namespace CSharpRepl.Services.Roslyn.Scripting;

/// <remarks>about as close to a discriminated union as I can get</remarks>
public abstract record EvaluationResult
{
    public sealed record Success(string Input, object ReturnValue, IReadOnlyCollection<MetadataReference> References) : EvaluationResult;
    public sealed record Error(Exception Exception) : EvaluationResult;
    public sealed record Cancelled() : EvaluationResult;
}
