# Spike #3 — ASP.NET Core DI-root capture (M2 primary path)

De-risks the **clean** DI-capture path from the plan: an `IHostingStartup` (activated by
`ASPNETCORE_HOSTINGSTARTUPASSEMBLIES`) registers an `IStartupFilter` that captures
`IApplicationBuilder.ApplicationServices` — the app's root `IServiceProvider` — with **no reflection**.
The engine (injected via the Spike #2 startup hook) then resolves the app's **real** singletons.

## Layout

| Project | Role |
|---|---|
| `Target.Web`        | Ordinary minimal-API app. Singleton `IOrderService` with live `PendingCount`. References nothing inspector-related. |
| `Spike3.Bootstrap`  | Injected via `DOTNET_STARTUP_HOOKS` (loads engine) **and** activated via `ASPNETCORE_HOSTINGSTARTUPASSEMBLIES` (`InspectorHostingStartup` + `ProviderCapture` capture the provider). No Roslyn ref; ASP.NET via `FrameworkReference`. |
| `Spike3.Contracts`  | Shared `InspectorRoots.Services`, `InspectorGlobals` (`Services` + `Get<T>()`), `IInspectorEngine`. |
| `Spike3.Engine`     | Roslyn engine, globals wired to the captured provider so scripts use `Services.GetRequiredService<T>()`. |

The bootstrap is the **same assembly** for both env vars — exactly as the plan specifies
(`DOTNET_STARTUP_HOOKS` = path to it, `ASPNETCORE_HOSTINGSTARTUPASSEMBLIES` = its name).

## Run

```pwsh
./Spikes/AspNetDiCapture/run.ps1
```

## What it proves (observed PASS)

```
[hook] Initialize() running (before Main)
[hosting-startup] InspectorHostingStartup.Configure running
[hosting-startup] captured root IServiceProvider via IStartupFilter
   [engine] references=87, registered live deps=87, skipped=0
   [inspector] PendingCount before HTTP = 0
   [inspector] issued 3/3 real HTTP POSTs to http://127.0.0.1:52199/order
   [inspector] PendingCount after HTTP  = 3
   [inspector] PASS: engine reads the SAME live singleton the request pipeline mutated (== 3)
   [inspector] requested graceful shutdown via captured provider
[target] ASP.NET app stopped cleanly
```

- **Hosting-startup activation works** when the assembly is injected purely via env vars and isn't
  referenced by the target (it resolves because it's already loaded as the startup hook).
- **`IStartupFilter` captures the root provider** cleanly (no reflection).
- **Identity proof (killer):** the engine, via the captured provider, observes a singleton mutated by
  the **real HTTP request pipeline** (`0 → 3`). Same instance, not a duplicate.
- **Captured provider is fully usable:** resolving `IHostApplicationLifetime` and calling
  `StopApplication()` shuts the app down gracefully.

## Notes

- Fixed test URL `http://127.0.0.1:52199` is hardcoded in both the target and the inspector demo
  (spike-only; the real product discovers the address or doesn't need to make HTTP calls).
- Works with minimal APIs / `WebApplication` (no `Startup` class needed) — `IStartupFilter` still runs.
- The DiagnosticListener fallback path (for older/edge hosts) is **not** exercised here; the primary
  path worked, which is what M2 leads with.
