# Assembly references, NuGet, and runtime loading

This document explains how csharprepl turns a `#r` directive into a usable assembly — both at
**compile time** (so Roslyn can bind names) and at **run time** (so the CLR can execute the code) — and
the set of traps in between: reference-vs-implementation assemblies, NuGet version unification, assembly
load contexts, shadow copying, and the type-identity rules that make or break a running submission.

It is the companion to [`ARCHITECTURE.md`](../../../ARCHITECTURE.md). Read that first for the big picture
(the two Roslyn worlds, the workspace/scripting split). This file zooms in on the reference/loading
machinery, which lives in `Roslyn/References/`, `Roslyn/MetadataResolvers/`, and `Nuget/`.

> **Why this is hard in one sentence:** a REPL has to reproduce, by hand and incrementally, the
> dependency resolution that `dotnet restore` does once up front *and* the assembly loading that the
> default host does for a normal app — except csharprepl loads everything into **non-default load
> contexts**, where none of the runtime's automatic version roll-forward applies.

### What "compile time" and "run time" mean in a REPL

csharprepl compiles **per submission**, not once per build. Every line you enter goes through two distinct
phases back-to-back: first Roslyn **compiles** the source into an in-memory IL assembly (binding names,
checking signatures, producing the diagnostics you see as you type), then the CLR **runs** that IL
(executing it, JIT-compiling each method on first call). So the normal compile/run split still applies — it
just happens once per line, and submission *N* compiles against the assemblies submissions *1…N-1* already
emitted and ran.

The split matters because the two phases want **different physical copies** of an assembly and resolve them
through **different machinery**: compile time needs *metadata only* (a reference assembly — API surface, no
method bodies — is enough), resolved by the `#r` resolvers (§3); run time needs the *real implementation*
loaded into a load context (§6). Most of the hard bugs in this area are a submission that **compiles fine
but throws at run time**, because those two independent resolution paths disagreed on which assembly (or
which version) a name maps to. Throughout this doc, "compile time" means the Roslyn source→IL step (it is
also what the editor uses for completion/highlighting *as you type*); the JIT's IL→machine-code step counts
as run time.

---

## 1. Two kinds of "assembly", two Roslyn worlds

Roslyn is used through two APIs that must be kept consistent (see `ARCHITECTURE.md`), and each wants a
*different physical copy* of the same logical assembly:

| World | API | Wants | Tracked in `AssemblyReferenceService` |
|---|---|---|---|
| **Workspace** (editor: completion, highlighting, tooltips) | Workspaces | **reference assemblies** (`ref/`, metadata-only facades) | `loadedReferenceAssemblies`, `referenceAssemblyPaths` |
| **Scripting** (actually executes code) | `CSharpScript` | **implementation assemblies** (`lib/`, real IL) | `loadedImplementationAssemblies`, `implementationAssemblyPaths` |

`AssemblyReferenceService` (`Roslyn/References/AssemblyReferenceService.cs`) is the stateful hub that
holds both sets and converts between them (`EnsureReferenceAssemblyWithDocumentation` maps an
implementation assembly back to its reference assembly + XML docs when one exists).

**Do not confuse this with the NuGet compile-vs-runtime asset split (§4).** They are different axes:
- *reference vs implementation* here is mostly about the **shared framework** (the editor binds against
  `Microsoft.NETCore.App.Ref`, the script runs against the real `Microsoft.NETCore.App`).
- *compile vs runtime* in §4 is about which DLLs a **NuGet package** contributes.

csharprepl deliberately compiles scripts against **implementation** assemblies (not ref assemblies) for
NuGet packages — that's why the NuGet path returns runtime assemblies (§4).

---

## 2. The shared framework

On startup `AssemblyReferenceService` loads a shared framework (`GetSharedFrameworkConfiguration` →
`LoadSharedFrameworkConfiguration`). `Microsoft.NETCore.App` is **always** loaded; `--framework
Microsoft.AspNetCore.App` adds another alongside it. Each framework contributes both a reference-assembly
path (e.g. `…/packs/Microsoft.NETCore.App.Ref/x.y.z/ref/netX.0`) and an implementation path (e.g.
`…/shared/Microsoft.NETCore.App/x.y.z`), located by `DotNetInstallationLocator` (global install first,
then the NuGet packs fallback).

A `#r`'d assembly that ships an adjacent **`*.runtimeconfig.json`** can pull in additional shared
frameworks: `AssemblyReferenceMetadataResolver.LoadSharedFramework` reads it and calls
`LoadSharedFrameworkConfiguration` for each framework named there. This is how `#r`'ing an ASP.NET Core
app's DLL makes the ASP.NET Core shared framework available (issues #399 / #468).

---

## 3. The reference pipeline: from `#r` to a `MetadataReference`

A submission's `#r` directives are resolved through **two parallel mechanisms** because Roslyn's
`MetadataReferenceResolver` can only return references for the *exact* string it was given, and can't
expand one `#r` into many references ([dotnet/roslyn#6900](https://github.com/dotnet/roslyn/issues/6900)).

### 3a. `MetadataReferenceResolver` (the synchronous, in-compilation path)
`CompositeMetadataReferenceResolver` chains three `IIndividualMetadataReferenceResolver`s
(wired in `ScriptRunner`'s constructor):

1. **`NugetPackageMetadataResolver`** — recognises `#r "nuget: …"` and returns a *dummy* reference (the
   real work is async, see 3b).
2. **`SolutionFileMetadataResolver`** — recognises `#r "*.csproj/*.sln/*.slnx"`; builds the project with
   `dotnet build` and returns its output + dependency closure.
3. **`AssemblyReferenceMetadataResolver`** — the fallback for plain/relative DLL paths
   (`#r "./Foo.dll"`). It resolves the file to a `MetadataReference` and, as a side effect of resolution,
   reads any adjacent `*.deps.json` (handing it to `ReplAssemblyLoader.RegisterDepsClosure` so the DLL's
   transitive dependencies resolve correctly at run time, §6) and `*.runtimeconfig.json` (to pull in
   shared frameworks, §2). It is **compile-time only** — it installs no runtime hook.

### 3b. `AlternativeReferenceResolver` (the async, "one `#r` → many references" path)
`CompositeAlternativeReferenceResolver.GetAllAlternativeReferences` runs **before** each submission is
compiled (`ScriptRunner.RunCompilation`). It splits the submission into lines and lets the NuGet and
solution resolvers each return a *set* of references asynchronously (restore / build are slow and
multi-output). The results are merged into `scriptOptions` and accumulate across submissions.

```
RunCompilation(text)
  └─ alternativeReferenceResolver.GetAllAlternativeReferences(text)   // async: nuget restore / sln build
        └─ NugetPackageMetadataResolver.ResolveAsync → NugetPackageInstaller.InstallAsync   // §4
  └─ scriptOptions = scriptOptions.WithReferences(RemoveDuplicateReferences(existing + new))  // §5
  └─ EvaluateStringWithStateAsync(...)                                // compiles + runs the submission
```

References accumulate in two places: `scriptOptions.MetadataReferences` and, after each successful
eval, `AssemblyReferenceService.LoadedImplementationAssemblies` (via `AddImplementationAssemblyReferences`,
which records each reference's **directory** into `implementationAssemblyPaths`).

---

## 4. NuGet resolution: restore, not a hand-rolled walk

`#r "nuget: Id, Version"` is handled by `NugetPackageInstaller` (`Nuget/NugetPackageInstaller.cs`), which
runs a **real, in-process `NuGet.Commands.RestoreCommand`** — the same engine `dotnet restore` uses.

### The model
Every `#r "nuget:"` in a session is a top-level `PackageReference` in **one logical project**. Each
install adds/updates the entry in `topLevelPackages` and **re-restores the whole accumulated set**. So
transitive versions unify across packages exactly the way a built app's restore unifies them
(highest-applicable, honoring version *ranges*) — there is never more than one version of an assembly in
the resolved closure. The resolved assemblies are read straight out of the restore's **assets file**
(`LockFile`): one source of truth. Resolving the whole set as one project is also what makes the runtime
pinning in §6 unambiguous (a single version per assembly).

### Details that matter
- **Version semantics.** A specified version (`#r "nuget: X, 1.2.3"`) is treated as a *minimum*
  (`[1.2.3, )`), like a project's `<PackageReference>`, so it can unify upward. No version → a floating
  `*` (or `*-*` when prereleases are enabled).
- **Compile vs runtime assets.** The lock file has a RID-less target (compile, `CompileTimeAssemblies`)
  and a RID-specific target (runtime, `RuntimeAssemblies` + `NativeLibraries`). csharprepl reads the
  **runtime (implementation) assemblies from the RID target** — that's both what runs and what backs
  compilation (see §1).
- **RID-specific assets.** Selecting `runtimes/<rid>/lib/...` (e.g. `System.Management`'s win build)
  requires the restore to be pointed at a RID graph via
  `TargetFrameworkInformation.RuntimeIdentifierGraphPath`. We reuse the `runtime.json` the tool already
  ships (`NugetHelper.RuntimeGraphPath`). Without it the restore only ever picks the RID-agnostic `lib/`
  assets. The requested RID is added via `RestoreRequest.RequestedRuntimes`.
- **Framework-provided packages.** On modern .NET, packages like `Microsoft.CSharp` resolve to an empty
  `_._` placeholder (the shared framework provides them). That correctly yields **zero** package
  references, and the install still reports success — "no assets" is not a failure. `_._` placeholders
  are skipped when building reference paths.
- **Native assets.** A package's `runtimes/<rid>/native` directory is registered with
  `NativeAssemblyResolver` (`AddNativeSearchDirectory`) so its unmanaged libraries are loadable at
  p/invoke time (issue #375).
- **Global packages folder.** Restore downloads to the standard global packages folder (honoring
  `NUGET_PACKAGES` / `nuget.config`), not a tool-private folder. Paths are resolved from
  `LockFile.PackageFolders` via `FallbackPackagePathResolver`.

### NuGet API gotchas (NuGet 7.6.0, the version pinned here)
- `RestoreCommandProviders` has no public `Create` — use `new RestoreCommandProvidersCache().GetOrCreate(...)`.
- `RestoreRequest`'s only public ctor is the 7-arg one (takes `PackageSourceMapping` and
  `LockFileBuilderCache`).
- `TargetFrameworkInformation.Dependencies` is `ImmutableArray<LibraryDependency>`; set the package id
  through `LibraryRange`, not `LibraryDependency.Name` (get-only).
- The restore needs `request.ProjectStyle = PackageReference`, a `DependencyGraphSpec`
  (`AddProject` + `AddRestore`), `AllowNoOp = false`, and the obj output dir to exist on disk.
- `RuntimeGraph`/`JsonRuntimeFormat` live in namespace `NuGet.RuntimeModel` but **physically in
  `NuGet.Packaging.dll`** — there is no `NuGet.RuntimeModel` 7.6.0 package.

> **Build/packaging note (MSBL001 + the SDK-resolved NuGet assemblies):** most `NuGet.*` packages are
> referenced `PrivateAssets="all"` so the compile-time reference stays but the runtime DLL ships from the
> **SDK** (resolved via `MSBuildLocator` at startup). That's why `NugetPackageInstaller` types only load
> once `MSBuildLocator.RegisterDefaults()` has run (`DotNetInstallationLocator`). In tests this means the
> `NugetPackageInstaller` / `EvaluationTests` nuget cases only pass when a `RoslynServices`-constructing
> test has run first in the same process (the full suite does; running them *in isolation* fails to load
> `NuGet.Versioning`).

---

## 5. Compile-time version unification

`AssemblyReferenceService.RemoveDuplicateReferences` collapses references that share an assembly identity
(simple name + culture + public-key-token) down to the **highest version**, mirroring NuGet restore, and
warns (once) about the dropped version. This is purely a **compile-time** dedup: it keeps the same type
from appearing under two assembly identities (which otherwise produces Roslyn's confusing CS1929 /
CS0433). The matching **run-time** binding is handled independently by `ReplAssemblyLoader`'s registry
(§6), which applies the same highest-version policy — the two layers are not coupled.

With the restore model (§4) the *nuget* closure is already single-version, so this rarely fires for
nuget. It earns its keep for the reference kinds that can independently drag in a different version:
`#r "*.csproj/*.sln"` closures and bare `#r "Foo.dll"`.

---

## 6. Runtime loading: `ReplAssemblyLoader` and assembly load contexts (the deep end)

Getting a `MetadataReference` is only half the job. When a submission *executes*, the CLR must load the
real assembly — and csharprepl loads into **non-default `AssemblyLoadContext`s**, where the runtime's
automatic, version-tolerant binding does **not** apply. This is where most "it compiles but throws at
runtime" bugs come from.

`ReplAssemblyLoader` (`Roslyn/References/ReplAssemblyLoader.cs`) owns all run-time loading of the
assemblies the REPL pulls in. It is the run-time counterpart to the compile-time
`AssemblyReferenceMetadataResolver`, and it has three parts: one load context, a name→path registry, and
a resolve fallback.

### The contexts in play
| ALC | Name | Holds |
|---|---|---|
| Default | `"Default"` | the framework + csharprepl's own assemblies |
| Roslyn's `InteractiveAssemblyLoader` (IAL) | `""` (empty) | the compiled **script submissions** and the references IAL loads for them |
| `ReplAssemblyLoader.loadContext` | `"CSharpReplLoadContext"` | everything the REPL loads at run time — the pinned nuget closure and any assemblies the resolve fallback loads |

Keeping all REPL-loaded assemblies in the **single** `loadContext` means a given assembly has one runtime
identity no matter which path loads it.

> **Why a dedicated context and not just Default?** The Default ALC *would* give version roll-forward for
> free (it binds by simple name, one version per name), which is tempting. But Default is already occupied
> by csharprepl's own closure — Roslyn especially, which the REPL is built on — and it can hold only one
> version per simple name. Loading user packages there would make a user's `#r`'d versions collide
> irreconcilably with the tool's own (e.g. `#r "nuget: Microsoft.CodeAnalysis.CSharp, 3.11.0"` would
> silently bind to whatever Roslyn csharprepl ships, with no way to get the requested version and no way to
> unload). Isolation is the point: it lets the user's program have a dependency graph independent of the
> REPL's implementation. The registry below is just the cost of reproducing Default's roll-forward *inside*
> that isolated island. (The shared framework is the deliberate exception — `System.*` / `Microsoft.NETCore.App`
> resolve from Default, which is why they're never put in the registry.)

> **The opposite choice, on purpose — the inspect engine.** csharprepl's inspect-a-running-process feature
> (`InjectedHook/.../InspectorEngine.cs`) faces the same isolate-vs-share decision and resolves it the other
> way. Injected into a target process, it builds its compilation references from the *target's* live,
> already-loaded assemblies (the target's Default ALC) and pins them with `RegisterDependency`, so the
> submissions bind to the target's **real live objects** — there the whole point is *not* to isolate. (Its
> injected Roslyn still sits in a separate ALC, but only to avoid clashing with any Roslyn the target itself
> loaded.) Same primitive, inverted goal: here we isolate user code from the host; there we deliberately fuse
> submissions to the target's ambient state.

### The registry and how a reference resolves at run time
The registry maps each assembly's **simple name** to a single canonical path (**highest version wins**,
mirroring restore). It is seeded eagerly:
- `RegisterPinned` — the restored nuget closure (also pinned into IAL; see below).
- `RegisterDepsClosure` — each `#r`'d DLL's deps.json, expanded to the **most RID-specific** compatible
  runtime file per dependency (e.g. `runtimes/win/lib/.../System.Management.dll` rather than the
  RID-agnostic `lib/` copy). Doing this at `#r` time is what lets the run-time fallback be a plain name
  lookup (issue #128).

A submission lives in the IAL context (`""`). When it touches a type whose assembly isn't loaded there,
resolution falls back: IAL context → Default ALC → (if both miss) `ReplAssemblyLoader`'s
**`AssemblyLoadContext.Resolving`** handler (`ResolveMissingAssembly`). The handler is attached to the
relevant contexts (Default and the submission context, plus the loader's own) via
`AssemblyLoadContextHook`, which hooks the contexts that exist and keeps hooking new ones as they appear
(the submission context is created later, on first eval) — the same pattern `NativeAssemblyResolver` uses
for native dlls. The handler does a single name lookup: the **registry** first, then a highest-version
by-name scan of `ImplementationAssemblyPaths`. This reproduces the version roll-forward (a request for
8.0.2 binds to a loaded 8.0.3) that a custom load context otherwise lacks. When it returns a *different
version* than requested, it prints the `Warning: Missing assembly … / Using instead …` you may have seen.

### The type-identity rule (memorize this)
> A `Type` is identified by **(assembly identity, namespace + name)** where "assembly identity" includes
> the **load context**. The *same physical DLL loaded into two different ALCs produces two different
> `Type`s.* Passing an instance of one to an API expecting the other fails with `MissingMethodException`,
> `TypeLoadException` ("… does not have an implementation"), or an `InvalidCastException`.

In a normal app this never bites: there's one Default ALC and the host's binder + `deps.json` give you
exactly one copy of each assembly, with version roll-forward. A custom ALC does **none of that
automatically** — which is why the single context + registry + pinning below exist.

### `RegisterDependency` — pinning a submission's direct references
`InteractiveAssemblyLoader.RegisterDependency(Assembly)` pins an *already-loaded* instance to its
identity, so IAL binds a submission's reference (and tolerantly, a lower-version request for the same
simple name) to **that exact instance** instead of loading its own copy. `RegisterPinned` loads each
nuget-closure assembly once into `loadContext` and pins it this way; IAL then serves that single instance
both for the submission's reference and for any transitive lower-version request (e.g. a package built
against EF Core 8.0.2 binding to the unified 8.0.3), so a type defined there has one identity, not two.

This is necessary in addition to the `Resolving` fallback, not redundant with it: IAL *proactively* loads
its own copy of a submission's **direct** reference, and `Resolving` only fires on a load *miss* — which
never happens for a direct reference — so pinning is the only lever that makes IAL agree on our instance.
Only the nuget closure is pinned; project/solution references are bound correctly by the existing
machinery and are served, if needed, by the by-name resolve fallback, never pinned. (The inspector engine,
`InjectedHook/.../InspectorEngine.cs`, uses the same `RegisterDependency` primitive to pin the target
process's live assemblies.)

### Shadow copying — load-bearing beyond file-unlock
`ScriptRunner` constructs `new InteractiveAssemblyLoader(new MetadataShadowCopyProvider())`. The
provider copies assembly **metadata to a temp directory** before the compiler reads it, so the original
files aren't locked. It is also **required for correct RID resolution of `#r`'d DLLs' transitive
dependencies**: by copying only the `#r`'d DLL's metadata (not its sibling files) to an isolated temp
dir, it makes IAL's same-directory probe for those transitive deps *miss*, so resolution falls through to
`ReplAssemblyLoader`'s `Resolving` handler, which picks the **RID-correct** variant from the registry.
With shadow copy off, IAL finds the RID-agnostic sibling copy in the `#r`'d DLL's own output folder first
and loads the wrong one. `MetadataShadowCopyProvider` is all-or-nothing — there is no per-assembly
scoping — so it stays on for everything. Guarded by `Evaluate_ResolveCorrectRuntimeVersionOfReferencedAssembly`
(#128).

---

## 7. Debugging assembly-loading problems

A `.csx` driven with `csharprepl --eval-file x.csx` (or piped stdin) is the fastest probe. Useful moves:

- **Enumerate instances and their contexts** — the single most useful diagnostic:
  ```csharp
  using System.Runtime.Loader;
  foreach (var a in AppDomain.CurrentDomain.GetAssemblies().Where(a => a.GetName().Name == "Microsoft.EntityFrameworkCore"))
      Console.WriteLine($"v{a.GetName().Version} hash={a.GetHashCode()} ALC='{AssemblyLoadContext.GetLoadContext(a)?.Name}' {a.Location}");
  ```
  More than one row for the same name = a split. Compare `GetHashCode()` / `ReferenceEquals` to confirm
  two `Type`s. (The test suite's `LoadedAssemblyInspector` helper turns this into an assertion.)
- `AssemblyLoadContext.All` lists every context; `AssemblyLoadContext.Default.Name == "Default"` (the
  empty-named context is IAL's; ours is `"CSharpReplLoadContext"`).
- **`MissingMethodException` is thrown at JIT of the *whole submission method***, so a `try/catch` *inside
  the same submission* can't catch it. Trigger the suspect call reflectively, or split it into a separate
  submission, to inspect state around it.
- Heavy reflection (`type.GetMethods()`) force-loads every parameter/return type and can surface a
  deeper `TypeLoadException` ("… does not have an implementation") that the plain call wouldn't — useful
  signal, but don't mistake it for the original failure.
- Run the test suite *in full* (or alongside a `RoslynServices` test) so `MSBuildLocator` is registered
  before NuGet types load (see §4 build note).

---

## 8. Invariants to preserve

- **One instance per assembly at run time.** If you add a new way to load package assemblies, route it
  through `ReplAssemblyLoader` (its single context + registry) or you'll reintroduce the type-identity split.
- **Keep shadow copy on** unless you've re-verified `Evaluate_ResolveCorrectRuntimeVersionOfReferencedAssembly`
  (it forces a `#r`'d DLL's transitive-dep resolution through the RID-aware fallback — see §6).
- **Only the nuget closure is pinned** (`RegisterDependency`); don't pin project/solution references.
- **Restore is the single source of truth** for nuget versions/paths; don't re-derive a dependency graph
  by hand.
- **Compile-time dedup and run-time binding stay decoupled** — `RemoveDuplicateReferences` (compile) and
  the `ReplAssemblyLoader` registry (run time) each apply highest-version independently; don't reintroduce
  a path-sharing coupling between them.
- **Keep the scripting↔workspace (implementation↔reference) consistency** — see `ARCHITECTURE.md` and the
  per-keystroke-latency note in `AGENTS.md`.
- **`NuGet.*` resolves from the SDK at run time** — don't start shipping additional `NuGet.*` runtime
  assemblies without checking MSBL001 and the R2R exclude list.

Key regression tests: `Evaluate_ResolveCorrectRuntimeVersionOfReferencedAssembly` (#128, RID resolution),
`Evaluate_TransitiveLowerVersionDependency_BindsToSingleRuntimeInstance` (#355, one runtime instance per
assembly), `Evaluate_ConflictingAssemblyReferenceVersions_BindToHighestAtRuntime` (#r DLL conflict),
`Evaluate_ConflictingPackageVersions_UnifiesToHighestVersion` (compile-time unification),
`Evaluate_SolutionReference_*`, `Evaluate_RunSharedFrameworkCode_DoesNotThrowAssemblyLoadException` (#414),
and `NugetPackageInstallerTests.*`.

---

## 9. A worked example: how the closure and state evolve

A session that references a project, a bare DLL, and a NuGet package, showing what each submission changes.
The pieces of state being tracked:

- **`topLevelPackages`** — the accumulated `#r "nuget:"` set; re-restored as a whole on each install (§4).
- **compile closure** (`scriptOptions.MetadataReferences`) — what Roslyn binds against (§3, §5).
- **impl search paths** (`implementationAssemblyPaths`) — directories the run-time by-name scan walks (§6).
- **registry** (`ReplAssemblyLoader`, name→path) — the run-time resolve table, seeded eagerly (§6).
- **loadContext** — assemblies actually *loaded* into the single run-time ALC (§6).
- **IAL** — the submission context: holds compiled submissions + the instances pinned via `RegisterDependency`.

**Start (after warm-up):** compile closure and impl search paths hold the shared framework; everything else
empty. Submissions execute in IAL (`""`), falling back to Default for the framework.

**① `#r "./Calc/Calc.csproj"`** — *alternative reference* (§3b). `SolutionFileMetadataResolver` runs
`dotnet build` and returns `Calc.dll` + its dependency closure. (Being the first alternative reference, it
also triggers the one-time empty base submission so the framework resolves from *implementation* assemblies,
§3b / #399.)
- compile closure **+= Calc.dll + closure**
- impl search paths **+= Calc's output dir** (after the submission succeeds)
- registry / loadContext / `topLevelPackages`: **unchanged** — project references are never registered or
  pinned; at run time they're served by the impl-path scan.

**② `#r "./libs/Vendor.dll"`** (ships an adjacent `Vendor.deps.json`) — *synchronous* resolve
(`AssemblyReferenceMetadataResolver`, §3a). Resolves the file, and as a side effect reads the deps.json and
calls `RegisterDepsClosure`.
- compile closure **+= Vendor.dll**
- impl search paths **+= libs/** (after success)
- registry **+= Vendor's transitive deps**, each recorded at its **most RID-specific** path (e.g.
  `runtimes/<rid>/lib/...`) — *path only, not yet loaded*
- loadContext: still empty (`Vendor.dll` itself is loaded by IAL, with shadow copy, only when run)

**③ `#r "nuget: Newtonsoft.Json, 13.0.3"`** — *alternative reference* (§4). `topLevelPackages` updated, the
whole set re-restored, `RegisterPinned` called on the result.
- `topLevelPackages` **= { Newtonsoft.Json: [13.0.3, ) }**
- compile closure **+= Newtonsoft.Json.dll**
- impl search paths **+= the package dir** (after success)
- registry **+= Newtonsoft.Json → …/13.0.3/…**
- loadContext **+= Newtonsoft.Json (loaded eagerly now)**, and that instance is **pinned into IAL**

**④ `using Calc; using Newtonsoft.Json; JsonConvert.SerializeObject(Calculator.Describe())`** — no new
references; compiles against the accumulated closure (framework + Calc + Vendor + Newtonsoft). At run time the
JIT pulls each dependency, and *now* the run-time machinery fires:
- `Calculator` → `Calc.dll` is a direct reference, so **IAL loads it** (shadow-copied). Any transitive dep of
  Calc that isn't already loaded → fallback → registry miss → **impl-path scan** finds it in Calc's output dir.
- `JsonConvert` → already **pinned in IAL**, so it binds to the single `loadContext` instance from ③ — no
  second copy (this is the #355 guarantee).
- If the code reaches one of Vendor's transitive deps → fallback → **registry hit** → that assembly is
  **lazily loaded into `loadContext`** from its RID-correct path.

**Two closures at end of session.** The **compile closure** (framework + Calc-closure + Vendor + Newtonsoft,
deduped to one version per identity by `RemoveDuplicateReferences`) is what every subsequent submission binds
against. The **run-time picture** is the single `loadContext` (Newtonsoft eager; Vendor-deps and scan hits
lazy) plus IAL (submissions + the pinned Newtonsoft), with project/DLL outputs reachable via the impl-path
scan — each assembly resolving to exactly one instance.
