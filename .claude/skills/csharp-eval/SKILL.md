---
name: csharp-eval
description: Run / execute C# snippets non-interactively with the csharprepl CLI to observe real runtime behavior — return values, exceptions, serialized output — and to probe how a NuGet package actually behaves when called. The complement to dotnet-inspect: that tool inspects static API surface without executing; this one runs code. Use whenever you need to know what C# *does*, not just what an API *looks like*.
---

# csharp-eval

Execute C# and see the result. `csharprepl` is a Roslyn-based C# REPL; in non-interactive mode it
evaluates a snippet, prints the value of the final expression, and exits — so you can verify runtime
behavior the same way you'd verify a shell one-liner.

## When to use this vs. dotnet-inspect

- **"What does this API *look like*?"** (signatures, members, docs, what changed between versions)
  → use **dotnet-inspect**. It reads metadata; it does not run anything.
- **"What does this code *do* when it runs?"** (the actual value, the exception it throws, the JSON it
  produces, how a package behaves) → use **csharp-eval**.

They compose well: inspect a method's signature with dotnet-inspect, then run it here to see its output.

## Use it to...

- Check runtime semantics / edge cases: `"a,,b".Split(',').Length`, default values, null handling,
  culture/format behavior, regex matches.
- Verify a LINQ chain or algorithm returns what you think before writing it into the project.
- See the actual serialized shape of an object (`JsonSerializer.Serialize(...)`).
- Probe how a NuGet package behaves at runtime — call it and look at the real output.
- Reproduce/confirm an exception and read its message and stack.

## Invocation

PowerShell-first (Windows). Output is plain text automatically when stdout is captured/redirected
(which it always is when a tool runs the command), so no color flag is needed — same as `python -c`.

### One-liners — `-e` (like `python -c`)

```powershell
csharprepl -e "Enumerable.Range(1, 5).Sum()"                 # -> 15
csharprepl -e 'System.Text.Json.JsonSerializer.Serialize(new { a = 1, b = 2 })'
```

The value of the final expression is **auto-printed** — no `Console.WriteLine` needed (it still works
if you want it). `-e` works cleanly when the C# uses only `"` strings.

### Multi-line, or code containing `'` char literals — `--eval-file` (like `python file.py`)

C# can't swap string delimiters the way Python can, so a snippet with a `' '` / `'\n'` char literal
collides with shell quoting. Don't fight the shell — write the snippet to a temp `.csx` (raw C#, zero
escaping) and run it:

```powershell
# write Demo.csx with the editor / Write tool, then:
csharprepl --eval-file Demo.csx
```

`--eval-file` runs the file and exits. (A *positional* `csharprepl Demo.csx` instead `#load`s the file
and then drops into the interactive REPL — it will hang waiting for input, so don't use it for
automation. Piping also works: `Get-Content Demo.csx -Raw | csharprepl`.)

### Referencing NuGet packages and assemblies

```powershell
csharprepl -e 'JsonConvert.SerializeObject(new[]{1,2,3})' -r "nuget: Newtonsoft.Json" -u Newtonsoft.Json
csharprepl -e '"PascalCaseInput".Humanize()' -r "nuget: Humanizer.Core, 3.0.10" -u Humanizer
```

- `-r "nuget: PackageName"` or `-r "nuget: PackageName, version"` — repeatable. (In-script
  `#r "nuget: ..."` also works when piping a file via stdin.)
- `-r <path-to.dll>` / `-r <path-to.csproj>` to reference a local assembly or project.
- `-u <Namespace>` adds a `using` (repeatable). `-f net10.0` selects the shared framework.
- The evaluation **result** is the last thing written to stdout. The first time a package is referenced
  NuGet prints a few restore-progress lines before it; cached runs print just the result.

## Gotchas

- **No state across calls.** Each invocation is a fresh process — variables, `using`s, and references
  do not carry over. Make every snippet self-contained (include its own `#r` / `using`).
- **First restore is slow.** The first time a package is referenced it's downloaded; subsequent runs
  are fast (cached under `~/.csharprepl/packages`).
- **Errors → stderr + nonzero exit.** Compilation and runtime errors are written to stderr and the
  process exits nonzero; capture stderr to see them.
- **`-e` and `--eval-file` are mutually exclusive.**
