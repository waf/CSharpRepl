---
name: csharprepl-connect
description: Connect to a running, connector-enabled .NET process with `csharprepl connect <pid>` and evaluate C# *inside* it — read and modify its live objects, statics, and DI services, and detour live methods (#replace/#wrap). Use to debug or probe a real running app's in-memory state, not static API surface (that's dotnet-inspect) or a throwaway snippet in a fresh process (that's csharp-eval). Dev/diagnostics only — code runs with the target's full privileges, never point it at production.
---

# csharprepl-connect

Evaluate C# **inside a separate, already-running .NET process** and see/modify its live state. `csharprepl`
injects a real Roslyn engine into a target you launched with the connector enabled; you then send code
non-interactively (same flags and output as the local REPL) and it runs in that process against its actual
in-memory objects.

## When to use this vs. csharp-eval vs. dotnet-inspect

- **"What's the live state of my running app?"** / **"Change a method's behavior in the running process"**
  → **this skill** (`csharprepl connect <pid>`). Code runs *inside* the target.
- **"What does this code do?"** (a self-contained snippet, fresh throwaway process) → **csharp-eval**.
- **"What does this API look like?"** (signatures, members, docs — no execution) → **dotnet-inspect**.

The eval mechanics here — `-e` / `--eval-file`, piped stdin, quoting, `-r "nuget: ..."`, the clean-stdout /
errors-to-stderr / nonzero-exit contract — are **identical to csharp-eval**; see that skill for those
details. This skill covers only what's different about connecting to a live process.

## ⚠️ Safety

Evaluated code runs with the **target process's full privileges** — it's RCE-equivalent for same-user code.
Only connect to a process **you control** for development/diagnostics. **Never enable the connector on, or
connect to, a production process.**

## 1. Enable the target (one-time, at launch)

The target only accepts connections if it was *started* with the connector hook — you cannot enable an
already-running process. `connect init` prints the env vars to set in the shell that launches it:

```
csharprepl connect init        # prints DOTNET_STARTUP_HOOKS=... and ASPNETCORE_HOSTINGSTARTUPASSEMBLIES=...
                               # auto-detects your shell; override with --shell bash|pwsh|cmd|fish
```

Set those env vars in the launching shell only (not system- or user-wide), then start the app normally
(e.g. `dotnet run`). It's now connectable for the life of that process.

## 2. Connect and evaluate

```
csharprepl connect list                                       # list connector-enabled processes + their PIDs
csharprepl connect <pid> -e 'System.Environment.ProcessId'    # -> the target's PID; confirms code runs in the target
csharprepl connect <pid> --eval-file probe.csx                # multi-line, same as local
echo 'SomeApp.Program.SomeStatic' | csharprepl connect <pid>  # piped stdin works too
```

- **Reach the target's state** by fully-qualified name (`MyApp.Program.SomeStatic`), or, when its DI provider
  was captured, via `services.GetRequiredService<T>()` / `Get<T>()` (the connect banner reports whether the
  DI provider was captured).
- **State persists across calls** (unlike local `csharp-eval`, where each run is a fresh process): the target
  holds the script-state chain, so a `var` declared in one `connect <pid> -e` invocation is usable in the
  next. This lets you build up state with one-shot calls.

## 3. Live method replacement

While connected you can detour a live method to a REPL-defined delegate, changing the running app's behavior
immediately:

- `#replace <Type.Method> with <delegate>` — swap the implementation.
- `#wrap <Type.Method> with <delegate>` — keep the original, callable via an `orig` first parameter.
- `#patches` — list active patches; `#revert <id>` / `#revert all` — undo them.

Instance methods take the instance as the first delegate parameter; a static method omits it. Define the
helper in one call, then `#replace` in the next — they share state across calls:

```
csharprepl connect <pid> -e 'decimal Half(MyApp.OrderService svc, int qty, decimal unit) => qty * unit * 0.5m;'
csharprepl connect <pid> -e '#replace MyApp.OrderService.CalculatePrice with Half'
csharprepl connect <pid> -e '#patches'        # list active patches
csharprepl connect <pid> -e '#revert all'     # undo them
```

- A command (`#replace`/`#wrap`/`#patches`/`#revert`) must be the **whole** submission — don't combine a
  definition and a command in one `-e` or one piped block, since collected stdin is sent as a single C#
  submission (so `#replace` would be compiled as invalid C#). To do it in one pipe, use `--streamPipedInput`,
  which evaluates line by line.
- Patches **persist in the target until reverted** (or it exits) — they outlive your disconnect, so revert when
  done.
- Not supported: generic methods, pointer params, and `#wrap` with by-ref parameters.

## Gotchas

- **Can't connect if not enabled.** `connect <pid>` fails unless the target was launched with the env vars
  from `connect init` (step 1). Use `connect list` to see what's actually connectable.
- **Self-contained single-file targets are rejected** — their assemblies are bundled in memory with no
  on-disk path, so the engine can't compile against them. Connect to a framework-dependent build instead. (A
  *framework-dependent* single-file app connects but can only reach its own types via reflection.)
- **Disconnecting leaves the target running.** `connect` exits cleanly; the process keeps going and you can
  reconnect — but any patches you applied stay in effect until reverted.
- Everything else (quoting, NuGet refs, errors→stderr, exit codes) works as in **csharp-eval**.
