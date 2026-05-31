---
name: csharp-eval
description: Run / execute C# snippets non-interactively with the csharprepl CLI to observe real runtime behavior — return values, exceptions, serialized output — and to probe how a NuGet package actually behaves when called. The complement to dotnet-inspect: that tool inspects static API surface without executing; this one runs code. Use whenever you need to know what C# *does*, not just what an API *looks like*.
---

# csharp-eval

Execute C# and see the result. `csharprepl` is a Roslyn-based C# REPL; run non-interactively it
evaluates a snippet, prints the value of the final expression as plain text, and exits.

## When to use this vs. dotnet-inspect

- **"What does this API *look like*?"** (signatures, members, docs, what changed between versions)
  → use **dotnet-inspect**. It reads metadata; it does not run anything.
- **"What does this code *do* when it runs?"** (the actual value, the exception it throws, the JSON it
  produces, how a package behaves) → use **csharp-eval**.

They compose well: inspect a method's signature with dotnet-inspect, then run it here to see its output.

## Use it to...

- Check runtime semantics / edge cases: how `string.Split` handles empty entries, default values,
  null handling, culture/format behavior, what a regex matches.
- Verify a LINQ chain or algorithm returns what you think before writing it into the project.
- See the actual serialized shape of an object (e.g. `JsonSerializer.Serialize(...)`).
- Probe how a NuGet package behaves at runtime — call it and look at the real output.
- Reproduce/confirm an exception and read its message.

## Running code

### One-liners — `--eval` or `-e`

```
csharprepl -e 'Enumerable.Range(1, 5).Sum()'                # -> 15
csharprepl -e 'DateTime.Parse("2026-01-31").DayOfWeek'      # -> Saturday
csharprepl -e 'new[] { 3, 1, 2 }.OrderBy(x => x).ToArray()' # -> int[3] { 1, 2, 3 }
```

**Tip: wrap the code in single quotes by default.** C# string literals use double quotes (`"..."`), so
single-quoting the snippet avoids escaping them. Switch to `--eval-file` (below) when the code itself
contains single quotes (C# char literals like `' '` or `'\n'`) rather than fighting the escaping.

The value of the final expression is **auto-printed** as plain text — no `Console.WriteLine` and no
color codes, just the value (collections render compactly, e.g. `int[3] { 1, 2, 3 }`). An explicit
`Console.WriteLine(...)` still works if you want to print more than the final value.

### Multi-line or quote-heavy code — `--eval-file`

For more than a quick expression — multiple statements, or code that's awkward to quote on the command
line (it contains quotes, etc.) — write it to a `.csx` file and run that. The file is raw C#, so there's
no command-line quoting to fight:

```
csharprepl --eval-file snippet.csx
```

`--eval-file` runs the file and exits. (Do NOT pass a `.csx` as a bare argument e.g. `csharprepl snippet.csx`,
that would load it and drop into the *interactive* REPL, hanging on input; use `--eval-file` for automation.)
Piping to stdin also evaluates and exits: `cat snippet.csx | csharprepl`.

### Referencing NuGet packages and assemblies

```
# reference a NuGet package, add a using, and call into it
csharprepl -e 'JsonConvert.SerializeObject(new[] { 1, 2, 3 })' -r 'nuget: Newtonsoft.Json' -u Newtonsoft.Json
```

- `-r "nuget: PackageName"` or `-r "nuget: PackageName, version"` — repeatable. (An in-script
  `#r "nuget: ..."` directive works too, e.g. inside an `--eval-file` snippet.)
- `-r <path-to.dll>` / `-r <path-to.csproj>` references a local assembly or project.
- `-u <Namespace>` adds a `using` (repeatable). `-f <framework>` selects the shared framework.
- The evaluation **result** is the last thing on stdout. The first time a package is referenced, NuGet
  prints a few restore-progress lines before it; cached runs print just the result.

## Gotchas

- **No state across calls.** Each invocation is a fresh process — variables, `using`s, and references
  do not carry over between runs. Make every snippet self-contained (include its own `#r` / `using`).
- **First restore is slow.** The first time a package is referenced it's downloaded; later runs are
  fast (cached under `~/.csharprepl/packages`).
- **Errors go to stderr with a nonzero exit code.** Compilation and runtime errors are written to
  stderr (stdout stays clean for the result), so check stderr when a run fails.
- **`-e` and `--eval-file` are mutually exclusive.**
