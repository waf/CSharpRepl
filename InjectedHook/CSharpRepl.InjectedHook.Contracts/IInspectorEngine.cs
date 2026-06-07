// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace CSharpRepl.InjectedHook.Contracts;

/// <summary>
/// The engine contract that crosses the ALC boundary. The bootstrap (default ALC) obtains an instance
/// from the isolated EngineALC and drives it. Successfully casting the engine instance to this interface
/// is itself proof that the contracts assembly is type-identical across the boundary.
/// </summary>
/// <remarks>
/// The engine maintains the persisted <c>ScriptState</c> submission chain, so calls are expected to be
/// serialized by the caller (one in-flight evaluation at a time).
/// </remarks>
public interface IInspectorEngine
{
    /// <summary>
    /// Evaluates a single C# submission, chaining onto the prior submission's state so locals, declared
    /// methods/types, and anonymous types persist across calls (full local-REPL parity). Never throws for
    /// compilation or runtime errors — those are returned as an <see cref="EvalResponse"/> of
    /// <see cref="ResultKind.Exception"/>.
    /// </summary>
    /// <param name="detailed">
    /// When true, the projected result includes an object's public members (the detailed view). When false,
    /// objects are projected as a one-line summary only, so property getters aren't invoked for the simple view.
    /// </param>
    Task<EvalResponse> EvalAsync(string code, bool detailed, CancellationToken cancellationToken);

    /// <summary>
    /// Returns the on-disk file paths of the target's loaded assemblies — the same set the engine builds its
    /// compilation references from. The controller uses these to seed its remote editor workspace (completion /
    /// semantic highlighting) so it sees the target's types. Single-file/in-memory assemblies are omitted.
    /// </summary>
    Task<IReadOnlyList<string>> GetReferencePathsAsync(CancellationToken cancellationToken);
}
