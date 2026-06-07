// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Collections.Generic;

namespace CSharpRepl.InjectedHook.Contracts;

/// <summary>
/// A serializable, transport-friendly projection of an evaluation's return value. The engine produces it
/// in the target process (where the live object lives) and the controller renders it through the existing
/// theming pipeline. The graph is depth-, breadth-, and length-limited and acyclic so it stays small and
/// safe to deserialize.
/// </summary>
/// <remarks>
/// The split is deliberate: the engine carries only theme-agnostic data (already-formatted scalar text plus
/// a <see cref="RemoteValueStyle"/> hint, or a structured member/element breakdown), and the controller
/// applies the user's theme. Scalars are formatted in the target with Roslyn's own <c>ObjectDisplay</c> — the
/// same formatter the local REPL's <c>PrimitiveFormatter</c> uses — so the text matches the local REPL exactly.
/// </remarks>
public sealed class RemoteValue
{
    /// <summary>The shape of the value, which selects how the controller renders it.</summary>
    public RemoteValueKind Kind { get; init; }

    /// <summary>
    /// The runtime type's friendly C# name (e.g. <c>List&lt;int&gt;</c>), or null when the value is null.
    /// Used for the object/collection header and the type-name colored summary.
    /// </summary>
    public string? TypeName { get; init; }

    /// <summary>
    /// For <see cref="RemoteValueKind.Scalar"/>: the formatted scalar text (quoted/escaped for strings and
    /// chars, invariant for numbers). For <see cref="RemoteValueKind.Object"/>: the one-line summary the
    /// local REPL shows at the simple level — an overridden <c>ToString()</c> / <c>DebuggerDisplay</c> result,
    /// or the friendly type name as a fallback. Empty for collections (rendered from <see cref="Items"/>).
    /// </summary>
    public string DisplayText { get; init; } = "";

    /// <summary>How the controller should color <see cref="DisplayText"/>.</summary>
    public RemoteValueStyle Style { get; init; }

    /// <summary>True when the value is null.</summary>
    public bool IsNull { get; init; }

    /// <summary>The public members of an <see cref="RemoteValueKind.Object"/> (null otherwise), depth-limited.</summary>
    public IReadOnlyList<RemoteMember>? Members { get; init; }

    /// <summary>The elements of a <see cref="RemoteValueKind.Collection"/> (null otherwise), count-limited.</summary>
    public IReadOnlyList<RemoteValue>? Items { get; init; }

    /// <summary>The collection's full element count, which may exceed <see cref="Items"/>.Count when truncated.</summary>
    public int? Count { get; init; }

    /// <summary>True when <see cref="Members"/>/<see cref="Items"/> were cut short by a depth/breadth/length limit.</summary>
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

/// <summary>The shape of a <see cref="RemoteValue"/>, selecting how the controller renders it.</summary>
public enum RemoteValueKind
{
    /// <summary>The value is null.</summary>
    Null,

    /// <summary>A primitive, string, char, or enum — rendered inline from <see cref="RemoteValue.DisplayText"/>.</summary>
    Scalar,

    /// <summary>A non-collection object — a summary line plus, at the detailed level, its <see cref="RemoteValue.Members"/>.</summary>
    Object,

    /// <summary>An <see cref="System.Collections.IEnumerable"/> — rendered from its <see cref="RemoteValue.Items"/>.</summary>
    Collection,
}

/// <summary>
/// A theme-agnostic coloring hint the controller maps to one of its theme's syntax-classification colors.
/// Kept independent of Roslyn's <c>ClassificationTypeNames</c> so the contracts assembly stays Roslyn-free.
/// </summary>
public enum RemoteValueStyle
{
    /// <summary>Default foreground (no special classification).</summary>
    None,

    /// <summary>A numeric literal.</summary>
    Number,

    /// <summary>A string or char literal.</summary>
    String,

    /// <summary>A language keyword (e.g. <c>true</c>, <c>false</c>, <c>null</c>).</summary>
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
