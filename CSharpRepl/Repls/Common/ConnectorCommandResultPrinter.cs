// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using CSharpRepl.InjectedHook.Contracts;
using CSharpRepl.Services.Remote.Commands;

namespace CSharpRepl.Repls.Common;

/// <summary>
/// Renders the wording for an <see cref="ConnectorCommandResult"/> (#replace / #wrap / #patches / #revert),
/// shared by the interactive <see cref="RemoteReadEvalPrintLoop"/> and the non-interactive
/// <see cref="RemotePipedInputEvaluator"/>. The caller supplies the output functions.
/// </summary>
internal static class ConnectorCommandResultPrinter
{
    public static void Print(ConnectorCommandResult result, Action<string> writeLine, Action<string> writeErrorLine)
    {
        switch (result)
        {
            case ConnectorCommandResult.UsageError usage:
                writeErrorLine($"Usage: {usage.Command.Usage}");
                break;

            case ConnectorCommandResult.Replaced { Response: var response } replaced when response.Ok:
                var arrow = replaced.Mode == PatchMode.Wrap ? "wrapped" : "patched";
                writeLine($"{arrow} {response.ResolvedMethod ?? replaced.Target}  ←  {replaced.Replacement}  (patch #{response.PatchId})");
                break;

            case ConnectorCommandResult.Replaced replaced:
                writeErrorLine($"{(replaced.Mode == PatchMode.Wrap ? ConnectorCommands.Wrap.Token : ConnectorCommands.Replace.Token)} failed: {replaced.Response.Error}");
                break;

            case ConnectorCommandResult.Listed { Response.Patches: { Count: 0 } }:
                writeLine("No active patches.");
                break;

            case ConnectorCommandResult.Listed listed:
                foreach (var patch in listed.Response.Patches)
                {
                    writeLine($"  #{patch.Id}  [{patch.Mode}]  {patch.Method}  ←  {patch.Replacement}");
                }
                break;

            case ConnectorCommandResult.Reverted { All: true } reverted:
                writeLine($"reverted {reverted.Response.RevertedCount} patch(es).");
                break;

            case ConnectorCommandResult.Reverted { Response.Ok: true } reverted:
                writeLine($"reverted patch #{reverted.RequestedId}.");
                break;

            case ConnectorCommandResult.Reverted reverted:
                writeErrorLine(reverted.Response.Error ?? "revert failed.");
                break;
        }
    }
}
