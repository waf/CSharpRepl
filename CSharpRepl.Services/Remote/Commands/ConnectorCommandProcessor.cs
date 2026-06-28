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
/// Parses connect-mode command lines (<c>#replace</c>/<c>#wrap</c>/<c>#patches</c>/<c>#revert</c>) and runs them
/// against the <see cref="RemoteSession"/>, returning the raw engine response for the REPL to render. Commands
/// are recognized case-insensitively while the casing of method names and replacement expressions is preserved.
/// </summary>
public sealed class ConnectorCommandProcessor
{
    private readonly RemoteSession session;

    public ConnectorCommandProcessor(RemoteSession session) => this.session = session;

    /// <summary>
    /// Runs <paramref name="commandText"/> if it's a connect command, returning its result; returns null when
    /// the text is not one of the commands, so the caller can fall through to normal evaluation. May surface an
    /// <see cref="IOException"/>/<see cref="OperationCanceledException"/> from the underlying session for the
    /// caller to handle.
    /// </summary>
    public async Task<ConnectorCommandResult?> TryExecuteAsync(string commandText, CancellationToken cancellationToken)
    {
        if (TryMatchArgument(commandText, ConnectorCommands.Replace, out var replaceArgs))
        {
            return await ReplaceAsync(replaceArgs, PatchMode.Replace, cancellationToken).ConfigureAwait(false);
        }
        if (TryMatchArgument(commandText, ConnectorCommands.Wrap, out var wrapArgs))
        {
            return await ReplaceAsync(wrapArgs, PatchMode.Wrap, cancellationToken).ConfigureAwait(false);
        }
        if (commandText.Equals(ConnectorCommands.Patches.Token, StringComparison.OrdinalIgnoreCase))
        {
            return new ConnectorCommandResult.Listed(await session.ListPatchesAsync(cancellationToken).ConfigureAwait(false));
        }
        if (commandText.Equals(ConnectorCommands.Revert.Token, StringComparison.OrdinalIgnoreCase) ||
            commandText.StartsWith(ConnectorCommands.Revert.Token + " ", StringComparison.OrdinalIgnoreCase))
        {
            return await RevertAsync(commandText[ConnectorCommands.Revert.Token.Length..].Trim(), cancellationToken).ConfigureAwait(false);
        }
        return null;
    }

    private async Task<ConnectorCommandResult> ReplaceAsync(string arguments, PatchMode mode, CancellationToken cancellationToken)
    {
        var info = mode == PatchMode.Wrap ? ConnectorCommands.Wrap : ConnectorCommands.Replace;
        var separator = arguments.IndexOf(ConnectorCommands.WithSeparator, StringComparison.OrdinalIgnoreCase);
        if (separator < 0)
        {
            return new ConnectorCommandResult.UsageError(info);
        }

        var target = arguments[..separator].Trim();
        var replacement = arguments[(separator + ConnectorCommands.WithSeparator.Length)..].Trim();
        if (target.Length == 0 || replacement.Length == 0)
        {
            return new ConnectorCommandResult.UsageError(info);
        }

        var response = await session.ReplaceAsync(target, replacement, mode, cancellationToken).ConfigureAwait(false);
        return new ConnectorCommandResult.Replaced(mode, target, replacement, response);
    }

    private async Task<ConnectorCommandResult> RevertAsync(string argument, CancellationToken cancellationToken)
    {
        if (argument.Equals("all", StringComparison.OrdinalIgnoreCase))
        {
            return new ConnectorCommandResult.Reverted(All: true, RequestedId: 0, await session.RevertAsync(0, all: true, cancellationToken).ConfigureAwait(false));
        }
        if (int.TryParse(argument, out var id))
        {
            return new ConnectorCommandResult.Reverted(All: false, RequestedId: id, await session.RevertAsync(id, all: false, cancellationToken).ConfigureAwait(false));
        }
        return new ConnectorCommandResult.UsageError(ConnectorCommands.Revert);
    }

    /// <summary>
    /// Matches an argument-taking command, requiring the trailing space before the argument (so a bare
    /// <c>#replace</c> with no argument falls through to evaluation, preserving the original dispatch behavior).
    /// </summary>
    private static bool TryMatchArgument(string commandText, ConnectorCommandInfo info, out string arguments)
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
