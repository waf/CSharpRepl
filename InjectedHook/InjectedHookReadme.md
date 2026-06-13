# Inspecting a Running Process (`csharprepl inspect`)

csharprepl can attach to a *separate, already-running* .NET application and evaluate C# inside it, reading and writing its live state (statics, DI singletons) with the same line-to-line experience as the local REPL.

This is not a debugger. A debugger pauses the target and asks the runtime to perform constrained expression evaluation; here, a real Roslyn scripting engine is injected *into* the target and runs unconstrained C#.

## How it works

Three projects under `InjectedHook/` are staged (together with their own private Roslyn closure) into the `inspector/` subdirectory next to the csharprepl tool by the `IncludeInspectorPayload` target in `CSharpRepl.csproj`:

| Project | Loaded into | Role |
|---|---|---|
| `CSharpRepl.InjectedHook` | target's default AssemblyLoadContext | The bootstrap. `StartupHook.Initialize()` runs before the target's `Main` (via `DOTNET_STARTUP_HOOKS`), must never throw, and references no Roslyn. It installs an assembly-resolving handler, subscribes to the hosting `DiagnosticListener` to capture the root `IServiceProvider` (`HostCapture`), creates the isolated engine ALC (`EngineHost`), and runs the transport server (`InspectorServer`) on a background thread. |
| `CSharpRepl.InjectedHook.ScriptEngine` | dedicated isolated ALC | The engine (`InspectorEngine`). Hosts `CSharpScript` and the persisted `ScriptState` submission chain. Its Roslyn lives entirely inside the isolated ALC, so it cannot collide with any Roslyn version the target itself uses. |
| `CSharpRepl.InjectedHook.Contracts` | default ALC, shared into the isolated ALC | The boundary types: `IInspectorEngine`, `InspectorGlobals`/`InspectorRoots`, `RemoteValue`, the `WireMessage` hierarchy, and the transport/framing (`InspectorTransport`, `MessageChannel`). Loaded once and resolved to that same instance from the engine ALC, so these types are *type-identical* on both sides. References no Roslyn. |

On first evaluation the engine snapshots the target's own loaded assemblies and builds its compilation references from them, so submissions compile against the target's real types and bind, at runtime, to the already-loaded live instances.

## Controller and inspector

When attached, the csharprepl process is a thin controller (`CSharpRepl/Repls/RemoteReadEvalPrintLoop.cs` + `CSharpRepl.Services/Remote/`). It compiles nothing for evaluation. The local REPL's "two Roslyn worlds kept in sync" idea is split across the two processes:

- The scripting world (execution, the persisted submission chain) lives in the target application, inside the engine.
- The workspace world (completion, highlighting, tooltips) stays in the controller / local REPL. On connect, it asks the inspector for the target's loaded-assembly paths and builds a second `RoslynServices` seeded with those paths plus the `InspectorGlobals`. This means that editor features are target-aware and don't need to communicate with the target app on each keystroke. The controller advances the Workspace Manager only when an `EvalResponse` reports `Committed == true`. This similar to how the local REPL advances its Workspace Manager only on successful evaluation.

Results are sent over the wire (a domain socket or named pipe) as a `RemoteValue` tree of data. A limited, theme-agnostic projection produced in the target applicaiton, and are rendered controller-side through the same theming pipeline as local output (`RemoteValueRenderer`).

## Wire protocol

One duplex connection per session (named pipe on Windows, Unix domain socket elsewhere). Every frame is a 4-byte little-endian length prefix + UTF-8 JSON body; messages are a `System.Text.Json` polymorphic hierarchy keyed on a `$kind` discriminator. The exchange:

1. → connect; ← `HandshakeMessage` (pid, runtime, inspector/protocol version, DI-captured flag, assembly availability).
2. → `ReferencesRequest`; ← `ReferencesResponse` (assembly paths for the controller's editor workspace).
3. Per submission: → `EvalRequest { Code, Detailed }`; ← `EvalResponse { Kind, Value/Exception, Committed }`. A `CancelMessage` may be sent mid-flight (Ctrl+C); the controller still waits for the response so framing stays in lock-step.
4. → `DisconnectMessage`; the target keeps running and the server loops to accept a future reconnect.

Inbound data is treated as untrusted: frame lengths are bounded (64 MB) and malformed frames surface as catchable exceptions, never a crash of the target.

## What doesn't work

- Self-contained single-file targets: every assembly (even corlib) is bundled in-memory with no on-disk metadata, so nothing can be compiled against. The controller refuses to start a session.
- Framework-dependent single-file targets: framework types work, but the app's *own* assemblies have no on-disk metadata, so typed access to its types fails to compile (CS0103). You can still reach its state via reflection (`Type.GetType("MyApp.Program, MyApp")`). The banner warns about this mode.
- Ctrl+C is cooperative: it cancels at points Roslyn observes (same as the local REPL). It cannot interrupt arbitrary running code — a submission stuck in a tight loop inside the target cannot be aborted (see "Known limitations").
- `#r` (NuGet/assembly references) inside a remote session is not supported.
