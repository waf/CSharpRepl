// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace CSharpRepl.Inspector.Contracts;

/// <summary>
/// Base type for every framed message on the wire. Serialized polymorphically by
/// <see cref="MessageChannel"/> via the <c>$kind</c> discriminator, so a single
/// <c>ReadAsync</c> returns the right concrete message.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "$kind")]
[JsonDerivedType(typeof(HandshakeMessage), "handshake")]
[JsonDerivedType(typeof(EvalRequest), "eval")]
[JsonDerivedType(typeof(EvalResponse), "result")]
[JsonDerivedType(typeof(ReferencesRequest), "references-request")]
[JsonDerivedType(typeof(ReferencesResponse), "references-response")]
[JsonDerivedType(typeof(DisconnectMessage), "disconnect")]
public abstract class WireMessage
{
}

/// <summary>
/// Sent by the inspector to the controller immediately on connect. Carries identity/version details so
/// the controller can show a banner and confirm it reached the right (non-stale) process. The
/// <see cref="SessionId"/> is a non-secret correctness token, not an authentication credential.
/// </summary>
public sealed class HandshakeMessage : WireMessage
{
    public int ProcessId { get; init; }
    public string ProcessName { get; init; } = "";
    public string RuntimeVersion { get; init; } = "";
    public string InspectorVersion { get; init; } = "";
    public int ProtocolVersion { get; init; }

    /// <summary>True when a root <see cref="System.IServiceProvider"/> was captured (M2). False means
    /// only statics/framework code are reachable.</summary>
    public bool DiProviderCaptured { get; init; }

    /// <summary>
    /// How the target was launched, which bounds what the inspector can do (M6). A self-contained single-file
    /// target can't be evaluated at all (no on-disk assemblies, not even corlib); a framework-dependent
    /// single-file target works for framework code + reflection but not typed access to the app's own types.
    /// </summary>
    public TargetAssemblyAvailability AssemblyAvailability { get; init; }

    /// <summary>Non-secret session identifier: process id plus a per-process instance GUID.</summary>
    public string SessionId { get; init; } = "";
}

/// <summary>
/// Describes whether the target's assemblies are reachable on disk, which determines what the inspector can do.
/// Detected at connect from whether the runtime/app assemblies have an on-disk <c>Assembly.Location</c>.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<TargetAssemblyAvailability>))]
public enum TargetAssemblyAvailability
{
    /// <summary>A normal launch (apphost or <c>dotnet App.dll</c>): assemblies are on disk; full typed access works.</summary>
    Normal,

    /// <summary>
    /// A framework-dependent single-file publish: the app's own assemblies are bundled (empty
    /// <c>Assembly.Location</c>) but the shared framework stays on disk. Framework code and reflection-based
    /// access to the target's live state work; typed access to the app's own types does not.
    /// </summary>
    FrameworkDependentSingleFile,

    /// <summary>
    /// A self-contained single-file publish: even the runtime assemblies are bundled, so no metadata reference
    /// (not even corlib) can be built and every evaluation fails. The controller refuses to start a session.
    /// </summary>
    SelfContainedSingleFile,
}

/// <summary>A request from the controller to evaluate one C# submission in the target.</summary>
public sealed class EvalRequest : WireMessage
{
    public string Code { get; init; } = "";

    /// <summary>
    /// True when the controller will render the detailed view (e.g. Ctrl+Enter). The engine projects an
    /// object's public members only when detailed, so it doesn't invoke the target's property getters for the
    /// one-line simple view — matching the local REPL, which reflects over members only for the detailed tree.
    /// </summary>
    public bool Detailed { get; init; }
}

/// <summary>The outcome of an <see cref="EvalRequest"/>.</summary>
public sealed class EvalResponse : WireMessage
{
    public ResultKind Kind { get; init; }

    /// <summary>The projected return value when <see cref="Kind"/> is <see cref="ResultKind.Value"/>.</summary>
    public RemoteValue? Value { get; init; }

    /// <summary>The projected exception when <see cref="Kind"/> is <see cref="ResultKind.Exception"/>.</summary>
    public RemoteException? Exception { get; init; }

    /// <summary>
    /// True when the submission committed to the persisted state chain (so the controller may advance its
    /// remote editor workspace — used in M5). False for compile/early failures that didn't extend the chain.
    /// </summary>
    public bool Committed { get; init; }

    public static EvalResponse FromValue(RemoteValue value, bool committed) =>
        new() { Kind = ResultKind.Value, Value = value, Committed = committed };

    public static EvalResponse Void(bool committed) =>
        new() { Kind = ResultKind.Void, Committed = committed };

    public static EvalResponse FromException(RemoteException exception, bool committed) =>
        new() { Kind = ResultKind.Exception, Exception = exception, Committed = committed };
}

/// <summary>
/// Asks the inspector for the file paths of the target's loaded assemblies, so the controller can seed its
/// remote editor workspace (IntelliSense + semantic highlighting) with the same references the engine compiles
/// against. Sent right after connect (M5). The engine resolves these lazily on first use, exactly as it builds
/// its own compilation reference set.
/// </summary>
public sealed class ReferencesRequest : WireMessage
{
}

/// <summary>The target's loaded-assembly file paths, used to build the controller's remote editor workspace.</summary>
public sealed class ReferencesResponse : WireMessage
{
    /// <summary>
    /// On-disk paths of the target's loaded, non-dynamic assemblies. Single-file/in-memory assemblies have no
    /// path and are omitted (the same set the engine can build a <c>MetadataReference</c> for).
    /// </summary>
    public IReadOnlyList<string> Paths { get; init; } = [];
}

/// <summary>Sent by the controller to end the session; the target keeps running.</summary>
public sealed class DisconnectMessage : WireMessage
{
}

/// <summary>The shape of an <see cref="EvalResponse"/>.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<ResultKind>))]
public enum ResultKind
{
    /// <summary>The submission produced a value (carried in <see cref="EvalResponse.Value"/>).</summary>
    Value,

    /// <summary>The submission ran but produced no value to display (e.g. a declaration or statement).</summary>
    Void,

    /// <summary>Compilation or execution failed (detail in <see cref="EvalResponse.Exception"/>).</summary>
    Exception,
}
