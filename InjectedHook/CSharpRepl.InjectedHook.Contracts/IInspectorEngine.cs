// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace CSharpRepl.InjectedHook.Contracts;

/// <summary>
/// The engine contract that crosses the ALC boundary: the bootstrap (default ALC) obtains an instance from
/// the isolated EngineALC and drives it.
///
/// - Successfully casting the engine to this interface proves the contracts assembly is type-identical
///   across the boundary.
/// - The engine holds the persisted ScriptState chain, so the caller must serialize calls (one in-flight
///   evaluation at a time).
/// </summary>
public interface IInspectorEngine
{
    /// <summary>
    /// Evaluates one C# submission, chaining onto the prior submission's state (locals, methods, and types
    /// persist across calls — full local-REPL parity).
    ///
    /// - Never throws for compile or runtime errors; those return as ResultKind.Exception.
    /// - detailed: when true, the projection includes an object's public members; when false, objects project
    ///   as a one-line summary so property getters aren't invoked for the simple view.
    /// </summary>
    Task<EvalResponse> EvalAsync(string code, bool detailed, CancellationToken cancellationToken);

    /// <summary>
    /// Returns the on-disk paths of the target's loaded assemblies — the same set the engine compiles against.
    ///
    /// - The controller seeds its remote editor workspace (completion / highlighting) with these.
    /// - Single-file/in-memory assemblies are omitted.
    /// </summary>
    Task<IReadOnlyList<string>> GetReferencePathsAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Detours a live method in the target to a REPL-defined replacement (see <see cref="ReplaceRequest"/>).
    /// Never throws for resolution/compile failures; those return as <c>Ok == false</c> with an Error.
    /// </summary>
    Task<ReplaceResponse> ReplaceMethodAsync(string targetMethod, string replacement, PatchMode mode, CancellationToken cancellationToken);

    /// <summary>Lists the patches currently applied by this engine.</summary>
    Task<PatchListResponse> ListPatchesAsync(CancellationToken cancellationToken);

    /// <summary>Undoes a patch by id, or every patch when <paramref name="all"/> is true.</summary>
    Task<RevertResponse> RevertAsync(int patchId, bool all, CancellationToken cancellationToken);
}
