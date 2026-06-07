## What this is

`csharprepl` is a cross-platform command-line C# REPL distributed as a .NET 10 global tool. It evaluates C# with Roslyn's scripting API and provides IntelliSense, syntax highlighting, NuGet/project/assembly references, object inspection, IL/lowered-C# disassembly (ILSpy), and OpenAI integration. The terminal UX (input, autocomplete menus, highlighting) comes from the separate **PrettyPrompt** library (same author, separate NuGet package/repo).

Everything targets **`net10.0`**. The SDK is pinned in `global.json` (10.0.0, `latestMajor`, prereleases allowed).

## Build, run, test

```console
dotnet build CSharpRepl.slnx                 # build the whole solution
dotnet run --project CSharpRepl              # run the REPL locally
dotnet test                                  # run the test suite (see gotchas below)
```

### Tests use Microsoft.Testing.Platform (MTP), not VSTest

The test runner is **Microsoft.Testing.Platform** with the **xUnit v3** runner (pinned in `global.json` under `"test"`). This changes the command line:

- `dotnet test` (optionally targeting the project: `dotnet test Tests/CSharpRepl.Tests/CSharpRepl.Tests.csproj`) goes through the **MTP** front-end, which has its own option set — run `dotnet test ... --help` to see it; don't assume `vstest.console`/MSBuild-runner flags.
- **To run a subset, use MTP's `*`-wildcard filter options** (wildcard works at the start and/or end of each pattern; repeat a flag to OR; the "simple" filters below can't be combined with the VSTest or query filter in one run):

  ```console
  dotnet test Tests/CSharpRepl.Tests/CSharpRepl.Tests.csproj --filter-class "*ShellDetectorTests"
  dotnet test Tests/CSharpRepl.Tests/CSharpRepl.Tests.csproj --filter-method "*ShellDetectorTests.DetectShell_IsBestEffortAndNeverThrows"
  dotnet test Tests/CSharpRepl.Tests/CSharpRepl.Tests.csproj --filter-namespace "CSharpRepl.Tests"
  ```

  `--filter-class` (FQ type), `--filter-method` (FQ `Type.Method`), `--filter-namespace`, and `--filter-trait "name=value"`; each has a `--filter-not-*` exclude variant. There's also `--filter "<VSTest syntax>"` and a path-form query filter, but only one filter *category* per run.

### Test-suite behavior to know about

- Heavy Roslyn/integration tests share `[Collection(nameof(RoslynServices))]` and run **serially** on purpose (`MSBuildLocator.RegisterDefaults()` and per-instance `AppDomain.AssemblyResolve` are process-global). The full suite is ~2 minutes.
- Process-based black-box tests are tagged `[Trait("Category","Integration")]`.
- Some tests spawn `dotnet build` / MSBuild subprocesses (solution/project references) and a few touch the network (NuGet install). These are the slow ones and can occasionally be flaky.
- The inspect-feature integration tests (`InspectorRoundTripTests`, `InspectorCancellationTests`, `RemoteEditorServicesTests`) **launch a real hooked child process** — the interactive PrettyPrompt loop itself cannot be driven without a TTY, so automated coverage stops at the engine round-trip and renderer.

### Benchmarks

BenchmarkDotNet project at `Tests/CSharpRepl.Benchmarks` (in the solution; `BenchmarkDotNet.Artifacts/` is gitignored):

```console
dotnet run -c Release --project Tests/CSharpRepl.Benchmarks -- --filter *AllocationBreakdown*
```

## Solution layout

- **`CSharpRepl/`** — the executable / global tool. `Program.cs` (CLI args, help, the read-eval-print loop), `CommandLine.cs` (argument parsing), `CSharpReplPromptCallbacks.cs` (wires PrettyPrompt callbacks to Roslyn services), `ReadEvalPrintLoop.cs`.
- **`CSharpRepl.Services/`** — the bulk of the logic. `Roslyn/` (scripting + workspace, see below), `Completion/`, `SyntaxHighlighting/`, `Theming/`, `Nuget/`, `SymbolExploration/` (Source Link), `CodeTransformation/` (IL disassembly + ILSpy lowering), and `Remote/` (controller side of the inspect feature).
- **`InjectedHook/`** — the three projects for the "inspect a running process" feature (see below).
- **`Tests/`** — `CSharpRepl.Tests` and `CSharpRepl.Benchmarks`.
- **`ARCHITECTURE.md`** — the authoritative deep-dive on design, including sequence/class diagrams for the inspect feature. Read it before substantial changes.

## Core architecture

csharprepl is an intermediary between **Roslyn** and **PrettyPrompt**. The single most important concept is that Roslyn is used through **two separate APIs that must be kept in sync**:

1. **Scripting world** — the C# Scripting API (`CSharpScript`) actually *executes* code and holds the `ScriptState` chain. Each submission is `ContinueWithAsync`'d onto the previous, so locals, declared methods, and types persist line-to-line. Lives in `Roslyn/Scripting/ScriptRunner.cs`.
2. **Workspace world** — the C# Workspaces API powers the *editor* features (highlighting, completion, tooltips, overloads, symbol lookup). The workspace is a **linked list of projects**, one per submitted line, each referencing the previous (`Roslyn/WorkspaceManager.cs`).

`Roslyn/RoslynServices.cs` is the façade over both. It manages Roslyn's slow initialization in the background (so the prompt stays responsive before init finishes) and, on each **successful** evaluation, advances the workspace with a new project/document so highlighting and completion see the latest state. When changing evaluation flow, preserve this scripting↔workspace consistency.

`#r` references (assemblies, NuGet packages, `.csproj`/`.sln`/`.slnx`) are resolved through `AssemblyReferenceService` + `Roslyn/MetadataResolvers/` (a `MetadataReferenceResolver` Roslyn extension point). This also handles shared-framework and implementation-vs-reference-assembly concerns.

### Performance note (per-keystroke latency)

Per-keystroke latency is dominated by syntax highlighting, which historically scaled linearly with submission count. The fix lives in `WorkspaceManager.UpdateCurrentDocumentAsync`: after setting the current document it `await`s `GetCompilationAsync()` and **discards the result**, forcing Roslyn's compilation tracker to its strongly-held final state so per-keystroke forks reuse it. Removing that line reintroduces an O(depth) regression — don't.

## Inspect-a-running-process feature (`InjectedHook/` + `CSharpRepl.Services/Remote/`)

csharprepl can attach to a *separate*, already-running .NET app and evaluate C# inside it, reading/writing its live state with full local-REPL parity. CLI: `csharprepl inspect init` (prints the env vars to launch the target with) then `csharprepl inspect <pid>`. It is **cooperative** (a real Roslyn engine is injected via a `DOTNET_STARTUP_HOOKS` startup hook — not a debugger) and **opt-in** only.

### Naming — read this before searching

The feature was renamed during development; **names are inconsistent across layers**, so search by the right token:

- **Folder / projects:** `InjectedHook/` containing `CSharpRepl.InjectedHook`, `CSharpRepl.InjectedHook.Contracts`, and `CSharpRepl.InjectedHook.ScriptEngine`.
  - Note: `ARCHITECTURE.md` calls the engine project `CSharpRepl.InjectedHook.Engine` — the actual project on disk is **`.ScriptEngine`**. Earlier planning docs use `CSharpRepl.Inspector.*`; those names no longer exist.
- **Classes:** still prefixed **`Inspector*`** (`InspectorServer`, `InspectorEngine`, `IInspectorEngine`, `InspectorClient`, `InspectorTransport`, `InspectorRoots`, `InspectorGlobals`).
- **User-facing verb:** **`inspect`**.

### The three injected projects and their assembly-load-context (ALC) roles

This is the crux of the design — the target may already load its own Roslyn, so the injected Roslyn must be isolated:

- **`CSharpRepl.InjectedHook`** (bootstrap) — injected into the target's **default ALC**. References **no Roslyn**. `StartupHook.Initialize()` (no namespace, `public static void`, must never throw and runs before the target's `Main`) installs an `AssemblyLoadContext.Default.Resolving` handler, creates the isolated engine ALC (`EngineHost.cs`), and starts the transport server (`InspectorServer.cs`).
- **`CSharpRepl.InjectedHook.ScriptEngine`** — loaded into a dedicated **isolated ALC** with its own Roslyn closure (`Microsoft.CodeAnalysis.CSharp.Scripting`). `InspectorEngine.cs` hosts `CSharpScript`, builds compilation references **lazily on first eval** from the *target's* loaded assemblies (so submissions bind to the target's real live objects), and projects results into a serializable `RemoteValue` tree.
- **`CSharpRepl.InjectedHook.Contracts`** — the shared boundary, loaded **once** in the default ALC and resolved to that same instance from the isolated ALC, so these types are **type-identical across the boundary**: `IInspectorEngine`, `InspectorGlobals`/`InspectorRoots`, `RemoteValue`, the `WireMessages` hierarchy, and `InspectorTransport`/`MessageChannel`. Also references no Roslyn.

### Controller side (`CSharpRepl.Services/Remote/` + `CSharpRepl/RemoteReadEvalPrintLoop.cs`)

When attached, csharprepl is a thin **controller**: it compiles nothing for evaluation, sends code strings, and renders the returned `RemoteValue` through the *same* theme/formatting pipeline as local output (`RemoteValueRenderer`). The **scripting world lives in the target** (the engine), but the **workspace world (completion/highlighting) stays in the controller** against a second, remote-configured `RoslynServices` seeded with the target's assembly paths + `InspectorGlobals` — so editor features need no per-keystroke round-trip. The controller advances that remote workspace only when an `EvalResponse` reports `Committed == true`.

### Wire protocol

A single duplex connection (named pipe on Windows, Unix domain socket elsewhere, **current-user only**). Every frame is a 4-byte little-endian length prefix + UTF-8 JSON body; messages are a `System.Text.Json` **polymorphic** `WireMessage` hierarchy keyed on a `$kind` discriminator (`MessageChannel`). Security model mirrors the .NET diagnostic port — OS access control, no secret. An inspector-enabled process is RCE-equivalent for same-user code and must never run in production.

### Packaging

`CSharpRepl.csproj`'s `IncludeInspectorPayload` target stages the full inspector payload (bootstrap + contracts + engine + Roslyn closure + `.deps.json`) into an **`inspector/` subdirectory** next to the tool (both on `dotnet run` and in the packed global tool), isolated so the engine's Roslyn never shadows the tool's. `inspect init` points `DOTNET_STARTUP_HOOKS` at `inspector/CSharpRepl.InjectedHook.dll`. The bootstrap is a `ProjectReference` with `ReferenceOutputAssembly="false"` (the tool must not link it).

## Gotchas

- **System.CommandLine is v3** (`3.0.0-preview.4`), driven via `RootCommand.Parse(...)` + `GetValue` (never `Invoke`). Two traps in `CommandLine.cs`: (1) a command with subcommands but no action makes `Parse` emit a "Required command was not provided" error — give every such command a no-op `SetAction(_ => 0)`; (2) `GetValue(argument)` throws on an unparseable token, so to validate yourself read the raw token (`parseResult.GetResult(arg)?.Tokens`) before `int.TryParse`. Recursive/global options use `option.Recursive = true`.
- **MSBuild assemblies must stay out of the output dir** (MSBL001). `Microsoft.Build.Framework` / `Microsoft.NET.StringTools` are referenced with `ExcludeAssets="runtime" PrivateAssets="all"`; MSBuild loads from the SDK at runtime via `MSBuildLocator`. When targeting a new SDK major, bump `NuGet.*` references in `CSharpRepl.Services` to match the SDK's bundled NuGet version.
- **`NuGet.*` loads from the SDK — except `NuGet.PackageManagement`.** In `CSharpRepl.Services.csproj` most `NuGet.*` packages are `PrivateAssets="all"`: the compile-time reference stays but the runtime DLL isn't shipped, so they resolve to the SDK's bundled copies at runtime (same `MSBuildLocator` mechanism as the MSBL001 fix above — avoids version skew). `NuGet.PackageManagement` (and the `NuGet.Resolver` it drags in) is the exception: the SDK doesn't bundle it, so we *do* ship it. That makes the output's reference closure intentionally incomplete, which breaks R2R crossgen2 (`-p:PackRidSpecific=true`) — so `NuGet.PackageManagement.dll`, `NuGet.Resolver.dll`, and `Microsoft.CodeAnalysis.Workspaces.MSBuild.dll` are listed in `PublishReadyToRunExclude` in `CSharpRepl.csproj`.
