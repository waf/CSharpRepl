// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace CSharpRepl.InjectedHook.Contracts;

/// <summary>
/// Base type for every framed message on the wire. Serialized polymorphically by MessageChannel via the
/// $kind discriminator, so a single ReadAsync returns the right concrete message.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "$kind")]
[JsonDerivedType(typeof(HandshakeMessage), "handshake")]
[JsonDerivedType(typeof(EvalRequest), "eval")]
[JsonDerivedType(typeof(EvalResponse), "result")]
[JsonDerivedType(typeof(ReferencesRequest), "references-request")]
[JsonDerivedType(typeof(ReferencesResponse), "references-response")]
[JsonDerivedType(typeof(CancelMessage), "cancel")]
[JsonDerivedType(typeof(DisconnectMessage), "disconnect")]
public abstract class WireMessage
{
}

/// <summary>
/// Source-generated JsonSerializerContext for the wire protocol.
///
/// Serializing the polymorphic WireMessage base writes the $kind discriminator; the generator follows the
/// type graph from there (derived messages, RemoteValue's recursive members/items, RemoteException, enums).
/// </summary>
[JsonSourceGenerationOptions(DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(WireMessage))]
internal sealed partial class WireJsonContext : JsonSerializerContext { }

/// <summary>
/// Sent by the inspector to the controller immediately on connect.
/// - Carries identity/version details for the controller's banner and to confirm it reached the right
///   (non-stale) process.
/// - SessionId is a non-secret correctness token, not an authentication credential.
/// </summary>
public sealed class HandshakeMessage : WireMessage
{
    public int ProcessId { get; init; }
    public string ProcessName { get; init; } = "";
    public string RuntimeVersion { get; init; } = "";
    public string InspectorVersion { get; init; } = "";
    public int ProtocolVersion { get; init; }

    /// <summary>True when a root IServiceProvider was captured; false means only statics/framework code are reachable.</summary>
    public bool DiProviderCaptured { get; init; }

    /// <summary>How the target was launched, which bounds what the inspector can do (see TargetAssemblyAvailability).</summary>
    public TargetAssemblyAvailability AssemblyAvailability { get; init; }

    /// <summary>Non-secret session identifier: process id plus a per-process instance GUID.</summary>
    public string SessionId { get; init; } = "";
}

/// <summary>
/// Whether the target's assemblies are reachable on disk, which determines what the inspector can do.
/// Detected from whether the runtime/app assemblies have an on-disk Assembly.Location.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<TargetAssemblyAvailability>))]
public enum TargetAssemblyAvailability
{
    /// <summary>A normal launch (apphost or dotnet App.dll): assemblies are on disk; full typed access works.</summary>
    Normal,

    /// <summary>
    /// A framework-dependent single-file publish: the app's own assemblies are bundled but the shared
    /// framework stays on disk. Framework code and reflection work; typed access to the app's own types does not.
    /// </summary>
    FrameworkDependentSingleFile,

    /// <summary>
    /// A self-contained single-file publish: even the runtime assemblies are bundled, so no metadata
    /// reference (not even corlib) can be built. The controller refuses to start a session.
    /// </summary>
    SelfContainedSingleFile,
}

/// <summary>A request from the controller to evaluate one C# submission in the target.</summary>
public sealed class EvalRequest : WireMessage
{
    public string Code { get; init; } = "";

    /// <summary>
    /// True when the controller will render the detailed view (e.g. Ctrl+Enter). The engine projects an
    /// object's public members only when detailed, so the simple view never invokes property getters.
    /// </summary>
    public bool Detailed { get; init; }
}

/// <summary>The outcome of an EvalRequest.</summary>
public sealed class EvalResponse : WireMessage
{
    public ResultKind Kind { get; init; }

    /// <summary>The projected return value when Kind is Value.</summary>
    public RemoteValue? Value { get; init; }

    /// <summary>The projected exception when Kind is Exception.</summary>
    public RemoteException? Exception { get; init; }

    /// <summary>
    /// True when the submission committed to the persisted state chain (so the controller may advance its
    /// remote editor workspace); false for compile/early failures that didn't extend the chain.
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
/// Asks the inspector for the target's loaded-assembly file paths, sent right after connect. The controller
/// seeds its remote editor workspace (completion + highlighting) with them — the same references the engine
/// compiles against.
/// </summary>
public sealed class ReferencesRequest : WireMessage
{
}

/// <summary>The target's loaded-assembly file paths, used to build the controller's remote editor workspace.</summary>
public sealed class ReferencesResponse : WireMessage
{
    /// <summary>On-disk paths of the target's loaded, non-dynamic assemblies; single-file/in-memory assemblies are omitted.</summary>
    public IReadOnlyList<string> Paths { get; init; } = [];
}

/// <summary>
/// Sent by the controller to cancel the in-flight evaluation (Ctrl+C).
///
/// - Cancellation is cooperative: it interrupts Roslyn-observed points, not arbitrary running user code.
/// - Keeps framing in sync: the controller still waits for the (cancelled or completed) result rather than
///   abandoning the read.
/// </summary>
public sealed class CancelMessage : WireMessage
{
}

/// <summary>Sent by the controller to end the session; the target keeps running.</summary>
public sealed class DisconnectMessage : WireMessage
{
}

/// <summary>The shape of an EvalResponse.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<ResultKind>))]
public enum ResultKind
{
    /// <summary>The submission produced a value (carried in EvalResponse.Value).</summary>
    Value,

    /// <summary>The submission ran but produced no value to display (e.g. a declaration or statement).</summary>
    Void,

    /// <summary>Compilation or execution failed (detail in EvalResponse.Exception).</summary>
    Exception,
}
