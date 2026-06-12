// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using CSharpRepl.InjectedHook.Contracts;

namespace CSharpRepl.Services.Remote;

/// <summary>
/// Seeds a <see cref="Roslyn.RoslynServices"/> for inspect mode: the editor services (completion, semantic
/// highlighting, overloads, formatting) run controller-side but must see the <em>target's</em> types and the
/// inspector globals, not the local REPL's. This carries the two things that differ from a local session —
/// the target's loaded-assembly paths and the globals type whose members are in scope for every submission.
/// </summary>
/// <remarks>
/// The workspace world stays in the controller (no per-keystroke pipe hop); only evaluation and result
/// rendering are routed to the target. The reference set is a snapshot taken at connect; assemblies the target
/// loads later (plugins, remote <c>#r</c>) aren't reflected until a future refresh — a documented v1 gap.
/// </remarks>
public sealed class RemoteEditorContext
{
    public RemoteEditorContext(IReadOnlyList<string> referencePaths, Type globalsType)
    {
        ReferencePaths = referencePaths;
        GlobalsType = globalsType;
    }

    /// <summary>The target's loaded-assembly file paths, as reported by the inspector engine.</summary>
    public IReadOnlyList<string> ReferencePaths { get; }

    /// <summary>
    /// The globals type whose members (e.g. <c>services</c>, <c>Get&lt;T&gt;()</c>) are in scope for every
    /// submission. Set as the workspace's host object type so the editor resolves those members; its assembly
    /// must be reachable from <see cref="ReferencePaths"/> (the target loads <see cref="InspectorGlobals"/>'s
    /// Contracts assembly in its default ALC, so it is).
    /// </summary>
    public Type GlobalsType { get; }
}
