# Spike #1 — ALC cross-context live-binding

Throwaway harness that de-risks the **crux** of the cooperative in-process Roslyn inspector
(see `update-the-plan-but-whimsical-stallman.md`, "ALC isolation & cross-context type identity").

It strips away everything conventional — no pipes, no separate process, no `DOTNET_STARTUP_HOOKS` —
and proves the single load-bearing unknown **in one process with two ALCs**:

> Can a Roslyn engine running in an **isolated** `AssemblyLoadContext` emit submission assemblies that
> (a) bind to **live object instances** in the **default** ALC, and (b) preserve **type identity** for
> both the shared contracts/globals type *and* the target's own types?

## Layout

| Project | ALC | Role |
|---|---|---|
| `Spike.Contracts` | Default (shared into EngineALC) | `InspectorRoots`, `InspectorGlobals`, `IInspectorEngine`, `EvalResult`. No Roslyn. |
| `Spike.TargetLib` | Default | Stand-in for the target app's domain DLL (`Counter`). The engine does **not** reference it — it's discovered at runtime. |
| `Spike.Engine`   | **Isolated EngineALC** | Hosts Roslyn `CSharpScript`; builds refs from the target's loaded assemblies; `RegisterDependency`. |
| `Spike.Host`     | Default | The harness. Plays both "target" and "controller": creates the live object, spins up `EngineLoadContext`, drives the chain, asserts. |

`Spike.Host` loads `Spike.Engine.dll` by reflection from a separate `engine/` subfolder (staged by the
`CopyEngine` MSBuild target) so the Roslyn closure genuinely loads into the isolated ALC.

## Run

```pwsh
dotnet build Spikes/AlcBinding/Host/Spike.Host.csproj
dotnet Spikes/AlcBinding/Host/bin/Debug/net10.0/Spike.Host.dll
```

Exit code `0` = all crux assertions passed.

## What it proves (all PASS)

- **Contracts type identity** across the boundary — the `(IInspectorEngine)` cast succeeds, and
  `typeof(InspectorGlobals)` is `ReferenceEquals` on both sides → contracts loaded exactly once.
- **Live binding** — a submission compiled inside the isolated EngineALC mutates the Host's real
  `Counter` instance; the default-ALC view sees `Count == 2`, and the returned object is
  `ReferenceEquals` to the Host's instance.
- **Full REPL parity** — `var c = …` then `c.Count` on the next line; `int Times10(int n) => …`
  then `Times10(c.Count)` — locals and declared methods persist via `ScriptState.ContinueWithAsync`.

## Findings that change the plan

1. **Submission assemblies load into a dedicated *unnamed* ALC that Roslyn creates — not Default, not EngineALC.**
   The in-submission probe shows the executing assembly is named `ℛ*<guid>#1-4` and its load context is
   non-null but **unnamed**. So Roslyn 5.3.0's `InteractiveAssemblyLoader` (itself living in EngineALC)
   spins up its own per-loader ALC for the per-line submissions. (My first reading of "Default" was the
   `?? "(Default)"` fallback firing on a *null Name* — corrected here.) Consequences:
   - The submission is *doubly* isolated from Default; it only reaches into Default for the specific
     **target types it references**, which resolve by the ALC's fallback-to-Default (see #2).
   - Roslyn **the library** stays isolated in EngineALC and submissions never reference it, so the
     "avoid clashing with the target's own Roslyn" motive holds — and is now proven (see Scenario C).
   - Submission assemblies accumulate in this loader-owned ALC for the session — note for memory.
   - This is observed behavior of Roslyn 5.3.0 on .NET 10, not a documented contract — **pin/re-verify
     on Roslyn upgrades.**

2. **`RegisterDependency` is *not* strictly load-bearing in this topology.**
   Scenario B (zero `RegisterDependency` calls) binds to the live instance just as well: the submission
   ALC fails to find `Spike.TargetLib` locally, falls back to the Default ALC, and finds the
   already-loaded copy there → same `Type` → live object. Keep `RegisterDependency` as a safety net (it
   pins the exact instance and covers target assemblies that are NOT already in Default's resolution
   path — e.g. plugins loaded into a non-default ALC), but the architecture does **not** hinge on it.

3. **Reference set grows with submissions.** Building references from
   `AssemblyLoadContext.Default.Assemblies` each round picks up the accumulating submission assemblies
   (12 → 26 across the run). The real engine should filter out submission/dynamic assemblies (or build
   the target reference set once) rather than re-enumerating Default blindly.

4. **Roslyn-version clash is a non-issue (Scenario C — the reason isolation exists).**
   With the target running its *own* Roslyn **v4.8.0** in the Default ALC, the engine's **v5.3.0** loads
   cleanly into EngineALC, live binding still works, and the target's 4.8.0 Roslyn keeps working before
   and after the engine runs. The isolated-ALC design does what it was chosen to do. This is the most
   important confirmation: it's the architectural justification for the whole EngineALC approach.

## What this spike deliberately does NOT cover (next de-risks)

- **Startup-hook injection into a real separate process** (`DOTNET_STARTUP_HOOKS` + the
  `Default.Resolving` probing trick). Conventional, but first time it's a real second process.
  → This is **Spike #2** (`../StartupHookInject`).
- **Single-file / empty-`Assembly.Location` degradation** — the detection code exists but no loaded
  assembly triggered it here (`skipped: 0`). Exercise it with a single-file or in-memory target.
- **Real DI capture** — `InspectorRoots.Service` holds a plain object, not a captured
  `IServiceProvider.GetRequiredService<T>()`. That's M2.

Covered as of the latest run: Roslyn-version clash (Scenario C, see Finding #4).
