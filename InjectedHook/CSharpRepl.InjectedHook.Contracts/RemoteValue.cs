// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Collections.Generic;

namespace CSharpRepl.InjectedHook.Contracts;

/// <summary>
/// A serializable, transport-friendly projection of an evaluation's return value, produced by the engine in
/// the target process (where the live object lives) and rendered by the controller through the existing
/// theming pipeline.
///
/// - The graph is depth-, breadth-, and length-limited and acyclic, so it stays small and safe to deserialize.
/// - The engine carries only theme-agnostic data (formatted scalar text plus a RemoteValueStyle hint, or a
///   structured member/element breakdown); the controller applies the user's theme.
/// - Roslyn's ObjectDisplay is internal and unreachable from the engine's isolated ALC, so scalars are
///   formatted by a small replicated formatter matching the local REPL's PrimitiveFormatter options
///   (quoted/escaped strings and chars, invariant-culture numbers).
/// </summary>
public sealed class RemoteValue
{
    /// <summary>The shape of the value, which selects how the controller renders it.</summary>
    public RemoteValueKind Kind { get; init; }

    /// <summary>The runtime type's friendly C# name (e.g. List&lt;int&gt;), or null when the value is null.</summary>
    public string? TypeName { get; init; }

    /// <summary>
    /// The display text, by kind:
    /// - Scalar: the formatted text (quoted/escaped strings and chars, invariant numbers).
    /// - Object: the one-line summary (overridden ToString, or the friendly type name as fallback).
    /// - Collection: empty (rendered from Items).
    /// </summary>
    public string DisplayText { get; init; } = "";

    /// <summary>How the controller should color DisplayText.</summary>
    public RemoteValueStyle Style { get; init; }

    /// <summary>True when the value is null.</summary>
    public bool IsNull { get; init; }

    /// <summary>The public members of an Object (null otherwise), depth-limited.</summary>
    public IReadOnlyList<RemoteMember>? Members { get; init; }

    /// <summary>The elements of a Collection (null otherwise), count-limited.</summary>
    public IReadOnlyList<RemoteValue>? Items { get; init; }

    /// <summary>The collection's full element count, which may exceed Items.Count when truncated.</summary>
    public int? Count { get; init; }

    /// <summary>True when Members/Items were cut short by a depth/breadth/length limit.</summary>
    public bool Truncated { get; init; }

    /// <summary>The null literal projection.</summary>
    public static RemoteValue Null { get; } = new() { Kind = RemoteValueKind.Null, IsNull = true, DisplayText = "null", Style = RemoteValueStyle.Keyword };
}

/// <summary>A named member (property/field) of a projected object.</summary>
public sealed class RemoteMember
{
    public string Name { get; init; } = "";
    public RemoteValue Value { get; init; } = RemoteValue.Null;
}

/// <summary>The shape of a RemoteValue, selecting how the controller renders it.</summary>
public enum RemoteValueKind
{
    /// <summary>The value is null.</summary>
    Null,

    /// <summary>A primitive, string, char, or enum — rendered inline from DisplayText.</summary>
    Scalar,

    /// <summary>A non-collection object — a summary line plus, at the detailed level, its Members.</summary>
    Object,

    /// <summary>An IEnumerable — rendered from its Items.</summary>
    Collection,
}

/// <summary>
/// A theme-agnostic coloring hint the controller maps to one of its theme's syntax-classification colors.
/// Kept independent of Roslyn's ClassificationTypeNames so the contracts assembly stays Roslyn-free.
/// </summary>
public enum RemoteValueStyle
{
    /// <summary>Default foreground (no special classification).</summary>
    None,

    /// <summary>A numeric literal.</summary>
    Number,

    /// <summary>A string or char literal.</summary>
    String,

    /// <summary>A language keyword (e.g. true, false, null).</summary>
    Keyword,

    /// <summary>A type name.</summary>
    TypeName,
}

/// <summary>
/// A serializable projection of an exception thrown while compiling or running a submission.
/// </summary>
public sealed class RemoteException
{
    /// <summary>The exception type's full name (or "CompilationError" for compile failures).</summary>
    public string TypeName { get; init; } = "";

    /// <summary>The exception message (or the joined compiler diagnostics for compile failures).</summary>
    public string Message { get; init; } = "";

    /// <summary>The full textual detail (ToString) — stack trace for runtime errors.</summary>
    public string Detail { get; init; } = "";
}
