# Spike #2 — cross-process startup-hook injection

De-risks the **activation path**: getting the inspector into a *real, separate, unmodified* target
process via `DOTNET_STARTUP_HOOKS`, and from there reading/writing the target's live state. Spike #1
already settled the ALC binding in-process; this spike's novel risk is the injection + resolution dance.

## The risk it proves out

The bootstrap (`Spike2.Bootstrap.dll`, the `CSharpRepl.Inspector` analog) is injected into the target's
**default ALC** by the runtime. But its only non-framework dependency (`Spike2.Contracts.dll`) lives in
the **bootstrap dir**, which is *not* on the target's probing path and *not* in the target's deps.json.
The fix — install a `Default.Resolving` handler pointing at the bootstrap dir — has a chicken-and-egg
problem: the handler is installed *inside* `StartupHook.Initialize()`, so the bootstrap must touch **zero
non-framework types** until the handler is live.

Solved with the **deferred-dependency pattern**:
- `StartupHook` has **no namespace** (host contract) and `Initialize()` uses only framework types until
  it has installed the `Default.Resolving` handler;
- it then calls `StartInspector(...)`, marked `[MethodImpl(NoInlining)]`, so that method's body — and the
  `Spike2.Contracts` / `Spike2.Bootstrap.EngineHost` references in it — are JIT-resolved only when called,
  i.e. *after* the handler is live.

## Layout

| Project | Role |
|---|---|
| `Target`            | An ordinary console app with mutable statics (`Counter`, `WriteProbe`). **References nothing inspector-related** — its source is unmodified, like a real target. |
| `Spike2.Bootstrap`  | Injected via `DOTNET_STARTUP_HOOKS`. `StartupHook` (no namespace) + `EngineHost` + `EngineLoadContext`. **No Roslyn reference** (asserted at runtime). |
| `Spike2.Contracts`  | Shared `IInspectorEngine` / `EvalResult` / `InspectorHost`. Resolved into the default ALC via the bootstrap's `Resolving` handler. |
| `Spike2.Engine`     | Roslyn engine (loaded into the isolated EngineALC). Staged next to the bootstrap DLL by `CopyEngine`. |

## Run

```pwsh
./Spikes/StartupHookInject/run.ps1
```

It builds the bootstrap (which pulls in Contracts + Engine + the Roslyn closure) and the target, sets
`DOTNET_STARTUP_HOOKS` to the absolute bootstrap path, and launches `dotnet Target.dll`.

## What it proves (observed PASS)

- **Hook runs before `Main`** — the `[hook]` lines print before `[target] Main starting`.
- **Bootstrap has no static Roslyn dependency** — `BootstrapReferencesRoslyn() == False`.
- **Resolution dance works** — Contracts/Engine resolve from the bootstrap dir via the `Default.Resolving`
  handler even though the target references nothing and has no relevant deps.json entries.
  No `DOTNET_ADDITIONAL_DEPS` / runtime store needed.
- **Roslyn isolated** — engine + Roslyn 5.3.0 run in `EngineALC`.
- **Live read across the process** — the injected engine reads `Target.Program.Counter` as it changes
  (`9 → 11 → 13 → 15`).
- **Live write across the process (killer)** — the injected engine sets `Target.Program.WriteProbe = 9999`
  and the target's *own* `Main` confirms it saw `9999`.

## Notes / hardening for the real implementation

- This target is **framework-dependent** (`dotnet Target.dll`). Self-contained / single-file targets are
  not exercised here — and single-file is the known reference-enumeration limitation (`Assembly.Location`
  empty) carried over from Spike #1.
- The `Default.Resolving` handler here loads **any** `<name>.dll` found in the bootstrap dir. The real
  implementation should **scope it to inspector assemblies** so it can't accidentally shadow the target's
  own resolution. (M6 hardening.)
- The inspector runs on a background thread driven by sleeps; the real product replaces that with the
  pipe server (M1) accepting a controller connection. The injection mechanism proven here is unchanged.

## Still not covered (remaining de-risks)

- **Transport/pipe + handshake + remote REPL loop** (M1) — conventional.
- **Real DI capture** via `IHostingStartup`/`IStartupFilter` on an ASP.NET host (M2) — this spike used a
  plain static, not a captured `IServiceProvider`.
- **Single-file / self-contained target** reference enumeration + injection.
