# Spike #4 — non-web Generic Host DI-root capture

**Upgrades a documented limitation into a solved capability.** The plan lists non-web Generic Host /
Worker Service DI capture as *future work* ("triggers neither the HostingStartup nor the DiagnosticListener
path"). This spike finds a clean mechanism that **does** work, with no source modification, no ASP.NET, and
no reflection into DI internals.

## The mechanism

`HostBuilder.Build()` and `HostApplicationBuilder.Build()` both write to a `DiagnosticListener` named
**`"Microsoft.Extensions.Hosting"`**:

| Event | Payload |
|---|---|
| `HostBuilding` | the builder (`HostBuilder` / `HostApplicationBuilder+HostBuilderAdapter`) — no provider yet |
| `HostBuilt`    | the `IHost` (`Microsoft.Extensions.Hosting.Internal.Host`) — **`.Services` is the root provider** |

(This is the same listener `HostFactoryResolver` and the EF Core / `dotnet` tools rely on, so it's a
well-trodden, stable contract — listener name + event names are effectively public.)

The bootstrap, from its startup hook **before `Main`**, subscribes to `DiagnosticListener.AllListeners`,
waits for the host listener, and on `HostBuilt` reflects `IHost.Services` off the payload. Reflection (not
a typed cast to the internal `Host`) keeps the bootstrap free of any `Microsoft.Extensions.*` reference.

## Layout

| Project | Role |
|---|---|
| `Target.Worker`     | Ordinary Generic Host console app. Singleton `ICounter` mutated by a `BackgroundService`. References nothing inspector-related. Supports both builders (`--app-builder`). |
| `Spike4.Bootstrap`  | Injected via `DOTNET_STARTUP_HOOKS`. `HostCapture` snoops the DiagnosticListener; no Roslyn / ASP.NET / Hosting reference. |
| `Spike4.Contracts`  | Shared `InspectorRoots.Services`, `InspectorGlobals`, `IInspectorEngine`. |
| `Spike4.Engine`     | Roslyn engine, globals wired to the captured provider. |

## Run

```pwsh
./Spikes/GenericHostDiCapture/run.ps1   # runs both builders
```

## What it proves (observed PASS — both builders)

```
[capture] found DiagnosticListener 'Microsoft.Extensions.Hosting'
[capture] hosting event 'HostBuilding' (payload: ...HostBuilder / HostBuilderAdapter)
[capture] hosting event 'HostBuilt'   (payload: Microsoft.Extensions.Hosting.Internal.Host)
[capture] captured root IServiceProvider from the 'HostBuilt' event
   [inspector] live WorkerApp.ICounter.Count = 7 / 9 / 11 / 13
   [inspector] PASS: engine reads the SAME live singleton the worker is mutating (count increased)
[worker] observed the inspector's write (Mark == 9999)
   [inspector] requested graceful shutdown via captured provider
[target] Generic Host stopped cleanly
```

- **Capture works for BOTH** `Host.CreateDefaultBuilder` **and** `Host.CreateApplicationBuilder`.
- **Identity (killer):** the engine reads a singleton whose value is climbing because the in-process
  `BackgroundService` is incrementing it — same instance, not a duplicate. Write-back (`Mark = 9999`) is
  observed by the worker.
- **Captured provider is fully usable:** `IHostApplicationLifetime.StopApplication()` shuts down cleanly.

## Caveats / hardening for the real implementation

- **Subscription must precede host build.** The startup hook guarantees this (it runs before `Main`).
  The `HostBuilt` write is gated on `IsEnabled()`, satisfied because we subscribe with `isEnabled => true`
  before the listener is created.
- **Reflect, don't cast.** The payload's concrete type (`...Internal.Host`) is internal — use
  `GetProperty("Services")`, never a typed cast.
- **First-host-wins.** We capture the first `HostBuilt` carrying a provider. Apps that build multiple
  hosts would need a policy; uncommon for CLI/worker apps.
- **Plan update:** the "non-web Generic Host DI capture is future work" limitation can be removed — this
  is a viable M2 path for Generic Host hosts, parallel to the ASP.NET `IStartupFilter` path (Spike #3).
