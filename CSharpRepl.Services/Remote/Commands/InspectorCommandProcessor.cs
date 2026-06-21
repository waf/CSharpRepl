// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using CSharpRepl.InjectedHook.Contracts;

namespace CSharpRepl.Services.Remote.Commands;

/// <summary>
/// Parses inspect-mode command lines (<c>#replace</c>/<c>#wrap</c>/<c>#patches</c>/<c>#revert</c>) and runs them
/// against the <see cref="RemoteSession"/>, returning the raw engine response for the REPL to render. Commands
/// are recognized case-insensitively while the casing of method names and replacement expressions is preserved.
/// </summary>
public sealed class InspectorCommandProcessor
{
    private readonly RemoteSession session;

    public InspectorCommandProcessor(RemoteSession session) => this.session = session;

    /// <summary>
    /// Runs <paramref name="commandText"/> if it's an inspect command, returning its result; returns null when
    /// the text is not one of the commands, so the caller can fall through to normal evaluation. May surface an
    /// <see cref="IOException"/>/<see cref="OperationCanceledException"/> from the underlying session for the
    /// caller to handle.
    /// </summary>
    public async Task<InspectorCommandResult?> TryExecuteAsync(string commandText, CancellationToken cancellationToken)
    {
        if (TryMatchArgument(commandText, InspectorCommands.Replace, out var replaceArgs))
        {
            return await ReplaceAsync(replaceArgs, PatchMode.Replace, cancellationToken).ConfigureAwait(false);
        }
        if (TryMatchArgument(commandText, InspectorCommands.Wrap, out var wrapArgs))
        {
            return await ReplaceAsync(wrapArgs, PatchMode.Wrap, cancellationToken).ConfigureAwait(false);
        }
        if (commandText.Equals(InspectorCommands.Patches.Token, StringComparison.OrdinalIgnoreCase))
        {
            return new InspectorCommandResult.Listed(await session.ListPatchesAsync(cancellationToken).ConfigureAwait(false));
        }
        if (commandText.Equals(InspectorCommands.Revert.Token, StringComparison.OrdinalIgnoreCase) ||
            commandText.StartsWith(InspectorCommands.Revert.Token + " ", StringComparison.OrdinalIgnoreCase))
        {
            return await RevertAsync(commandText[InspectorCommands.Revert.Token.Length..].Trim(), cancellationToken).ConfigureAwait(false);
        }
        return null;
    }

    private async Task<InspectorCommandResult> ReplaceAsync(string arguments, PatchMode mode, CancellationToken cancellationToken)
    {
        var info = mode == PatchMode.Wrap ? InspectorCommands.Wrap : InspectorCommands.Replace;
        var separator = arguments.IndexOf(InspectorCommands.WithSeparator, StringComparison.OrdinalIgnoreCase);
        if (separator < 0)
        {
            return new InspectorCommandResult.UsageError(info);
        }

        var target = arguments[..separator].Trim();
        var replacement = arguments[(separator + InspectorCommands.WithSeparator.Length)..].Trim();
        if (target.Length == 0 || replacement.Length == 0)
        {
            return new InspectorCommandResult.UsageError(info);
        }

        var response = await session.ReplaceAsync(target, replacement, mode, cancellationToken).ConfigureAwait(false);
        return new InspectorCommandResult.Replaced(mode, target, replacement, response);
    }

    private async Task<InspectorCommandResult> RevertAsync(string argument, CancellationToken cancellationToken)
    {
        if (argument.Equals("all", StringComparison.OrdinalIgnoreCase))
        {
            return new InspectorCommandResult.Reverted(All: true, RequestedId: 0, await session.RevertAsync(0, all: true, cancellationToken).ConfigureAwait(false));
        }
        if (int.TryParse(argument, out var id))
        {
            return new InspectorCommandResult.Reverted(All: false, RequestedId: id, await session.RevertAsync(id, all: false, cancellationToken).ConfigureAwait(false));
        }
        return new InspectorCommandResult.UsageError(InspectorCommands.Revert);
    }

    /// <summary>
    /// Matches an argument-taking command, requiring the trailing space before the argument (so a bare
    /// <c>#replace</c> with no argument falls through to evaluation, preserving the original dispatch behavior).
    /// </summary>
    private static bool TryMatchArgument(string commandText, InspectorCommandInfo info, out string arguments)
    {
        var prefix = info.Token + " ";
        if (commandText.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            arguments = commandText[prefix.Length..];
            return true;
        }
        arguments = "";
        return false;
    }
}
