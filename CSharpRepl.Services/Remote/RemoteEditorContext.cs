// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using CSharpRepl.InjectedHook.Contracts;
using CSharpRepl.Services.Completion;
using Microsoft.CodeAnalysis.Host;
using Microsoft.CodeAnalysis.Host.Mef;

namespace CSharpRepl.Services.Remote;

/// <summary>
/// Seeds a <see cref="Roslyn.RoslynServices"/> for connect mode: the editor services (completion, semantic
/// highlighting, overloads, formatting) run controller-side but must see the <em>target's</em> types and the
/// connector globals, not the local REPL's. This carries the two things that differ from a local session —
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

    // The default Roslyn MEF host plus this assembly, so the connect-only completion provider
    // (ReplaceMethodCompletionProvider, which powers #replace/#wrap autocomplete) is discovered. A process-wide
    // singleton built on first use: reconnects don't recompose the MEF catalog, and the local REPL (which uses the
    // cached MefHostServices.DefaultHost) never pays for it.
    private static readonly Lazy<HostServices> editorHostServices = new(() =>
        MefHostServices.Create(MefHostServices.DefaultAssemblies.Add(typeof(ReplaceMethodCompletionProvider).Assembly)));

    /// <summary>The target's loaded-assembly file paths, as reported by the connector engine.</summary>
    public IReadOnlyList<string> ReferencePaths { get; }

    /// <summary>
    /// The Roslyn workspace host the editor services should use in connect mode: the default providers plus the
    /// connect-only command-completion provider. Supplied to the <c>WorkspaceManager</c>; local sessions pass no
    /// host and fall back to <see cref="MefHostServices.DefaultHost"/>.
    /// </summary>
    internal HostServices EditorHostServices => editorHostServices.Value;

    /// <summary>
    /// The globals type whose members (e.g. <c>services</c>, <c>Get&lt;T&gt;()</c>) are in scope for every
    /// submission. Set as the workspace's host object type so the editor resolves those members; its assembly
    /// must be reachable from <see cref="ReferencePaths"/> (the target loads <see cref="ConnectorGlobals"/>'s
    /// Contracts assembly in its default ALC, so it is).
    /// </summary>
    public Type GlobalsType { get; }
}
