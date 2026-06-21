// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using CSharpRepl.InjectedHook.Contracts;

namespace CSharpRepl.Services.Remote.Commands;

/// <summary>
/// The outcome of running an inspect-mode command. A closed hierarchy carrying the raw engine responses so the
/// REPL — not this service — decides how to render them.
/// </summary>
public abstract record InspectorCommandResult
{
    private InspectorCommandResult() { }

    /// <summary>The argument was missing or malformed; the REPL should show <see cref="Command"/>'s usage.</summary>
    public sealed record UsageError(InspectorCommandInfo Command) : InspectorCommandResult;

    /// <summary>A <c>#replace</c>/<c>#wrap</c> completed; inspect <see cref="Response"/>.Ok for success vs failure.</summary>
    public sealed record Replaced(PatchMode Mode, string Target, string Replacement, ReplaceResponse Response) : InspectorCommandResult;

    /// <summary>A <c>#patches</c> listing.</summary>
    public sealed record Listed(PatchListResponse Response) : InspectorCommandResult;

    /// <summary>A <c>#revert</c> result. <see cref="RequestedId"/> is meaningful only when not <see cref="All"/>.</summary>
    public sealed record Reverted(bool All, int RequestedId, RevertResponse Response) : InspectorCommandResult;
}
