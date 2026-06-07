# Spike #5 — single-file target degradation

Characterizes what happens when the target is published with `PublishSingleFile=true`, where bundled
assemblies report an **empty `Assembly.Location`** so the engine can't build `MetadataReference`s from
disk. The bootstrap/engine/Roslyn ship **externally** (pointed at by `DOTNET_STARTUP_HOOKS`), so they are
never bundled; only the *target's* assemblies are.

## Run

```pwsh
./Spikes/SingleFileTarget/run.ps1   # publishes + runs both framework-dependent and self-contained
```

## Result: a three-tier gradient

| Target shape | Injection | `1+1` (framework eval) | Typed access to target types | Reflection access to live state |
|---|---|---|---|---|
| Normal (not single-file) | ✅ | ✅ | ✅ (full IntelliSense) | ✅ |
| **Framework-dependent single-file** | ✅ | ✅ | ❌ `CS0103` (app DLL bundled, no ref) | ✅ |
| **Self-contained single-file** | ✅ | ❌ *no metadata ref possible* | ❌ | ❌ |

### Framework-dependent single-file — **degraded but usable**
- Injection works; the hook runs before `Main` in the real single-file process.
- Only the **app's own assembly** (`Target.Single`) is bundled → empty Location → skipped (1 of 13).
  The shared framework is on disk, so corlib + `System.*` are still referenceable (12 of 13).
- `1 + 1` ✅. Typed `Target.Single.Program.Counter` ❌ (`CS0103` — no metadata ref to the bundled app DLL).
- **Reflection reaches live state:** `Type.GetType("...,Target.Single").GetField("Counter").GetValue(null)`
  returned `10` then `13` (climbing = live), and a reflection `SetValue` wrote `WriteProbe = 9999`, which
  the target's `Main` confirmed. So you lose typed access / completion for the target's **own** types, but
  framework code works and live state is reachable via reflection.

### Self-contained single-file — **engine non-functional**
- Injection still works, but **everything is bundled, including `System.Private.CoreLib`** (empty Location).
  Only 2 of 13 assemblies were referenceable.
- Roslyn can't even build the implicit core reference → **every** eval (even `1 + 1`) fails with
  `Can't create a metadata reference to an assembly without location.`
- This is a hard wall, not a partial degrade.

## Recommendations for the real implementation

1. **Detect at connect, report clearly, don't spew per-eval errors.**
   - If `typeof(object).Assembly.Location` is empty → **self-contained single-file**: refuse with a clear
     "inspector can't run against a self-contained single-file target (no metadata references available)."
   - Else if the target's *entry/app* assemblies have empty Location → **framework-dependent single-file**:
     connect in a **degraded "reflection mode"** banner — framework eval works; the target's own types
     aren't directly nameable (no completion for them); reach live state via reflection.
2. **Possible future mitigation (not v1):** recover bundled assembly bytes (extract the bundle, or read the
   loaded PE image from memory) and use `MetadataReference.CreateFromImage(bytes)` instead of `…FromFile`.
   For framework-dependent that's only the app assemblies; for self-contained it's everything (incl. corlib).
3. This matches the plan's stance ("document as a limitation; detect and report clearly") — now with the
   exact boundaries: framework-dependent = reflection-mode, self-contained = unsupported.
