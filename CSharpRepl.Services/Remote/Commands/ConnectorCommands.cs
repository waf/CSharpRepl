// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Collections.Generic;

namespace CSharpRepl.Services.Remote.Commands;

/// <summary>
/// Static metadata for one connect-mode REPL command. The single source of truth for its token, whether it
/// takes an argument, its usage line, and its help/completion description — consumed by the command processor,
/// the completion provider, and the prompt's completion items.
/// </summary>
public sealed record ConnectorCommandInfo(string Token, bool RequiresArgument, string Usage, string Description);

/// <summary>The connect-mode commands (<c>#replace</c>/<c>#wrap</c>/<c>#patches</c>/<c>#revert</c>) and their canonical text.</summary>
public static class ConnectorCommands
{
    /// <summary>Separates the target method from the replacement expression in <c>#replace</c>/<c>#wrap</c>.</summary>
    public const string WithSeparator = " with ";

    public static readonly ConnectorCommandInfo Replace = new(
        Token: "#replace",
        RequiresArgument: true,
        Usage: "#replace <Namespace.Type.Method> with <expression>",
        Description: "Replace a live method in the target with a method you define in the REPL. Signature must match. Usage: #replace <Namespace.Type.Method> with <yourMethod>");

    public static readonly ConnectorCommandInfo Wrap = new(
        Token: "#wrap",
        RequiresArgument: true,
        Usage: "#wrap <Namespace.Type.Method> with <expression>",
        Description: "Wrap a live method: the replacement's first parameter is an 'orig' delegate (Func/Action) that invokes the original. Usage: #wrap <Namespace.Type.Method> with <yourMethod>");

    public static readonly ConnectorCommandInfo Patches = new(
        Token: "#patches",
        RequiresArgument: false,
        Usage: "#patches",
        Description: "List the method replacements currently applied to the target.");

    public static readonly ConnectorCommandInfo Revert = new(
        Token: "#revert",
        RequiresArgument: false,
        Usage: "#revert <id> | #revert all",
        Description: "Undo a method replacement, restoring the original. Usage: #revert <id> or #revert all");

    public static readonly IReadOnlyList<ConnectorCommandInfo> All = [Replace, Wrap, Patches, Revert];
}
