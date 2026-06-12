# Vendored Roslyn code

The `Microsoft.CodeAnalysis*` directories are trimmed vendored copies of source from
the [dotnet/roslyn](https://github.com/dotnet/roslyn) repository (vendored in October
2022, commit `e55cbc0` of this repo). They keep their original namespaces and license
headers (MIT, .NET Foundation).

## Why vendor instead of referencing the NuGet packages?

All of these types are `internal` in Roslyn, so they cannot be reached through the
`Microsoft.CodeAnalysis.*` packages. They are the building blocks of Roslyn's
scripting `ObjectFormatter`, which is neither public nor extensible enough for the
styled/colored output CSharpRepl needs:

- `Microsoft.CodeAnalysis.CSharp/ObjectDisplay.cs` — formats primitive values as C#
  literals. The public `SymbolDisplay.FormatPrimitive` API is not a substitute: it
  lacks `ObjectDisplayOptions.IncludeTypeSuffix` (e.g. `3.5f`, `5m`, `123UL`) and
  `IncludeCodePoints`, which are visible in REPL output.
- `Microsoft.CodeAnalysis.Scripting.Hosting/ObjectFormatterHelpers.cs` — the
  reflection machinery behind `[DebuggerDisplay]` / `[DebuggerTypeProxy]` support.
- `Microsoft.CodeAnalysis.CSharp.Symbols/GeneratedName*.cs` — detects
  compiler-generated names so that closure/backing-field members can be hidden and
  async state-machine types can be displayed as their source method name.
- `Microsoft.CodeAnalysis.PooledObjects/*` — pooling infrastructure used by the
  files above.

## What was changed from upstream

The files are intentionally *trimmed* copies: members unused by CSharpRepl have been
deleted, and a few upstream classes were merged (e.g. `MemberFilter.cs` is
`CommonMemberFilter` + `CSharpMemberFilter`). There are no behavioral additions in
these directories — all CSharpRepl-specific formatting (syntax highlighting,
`StyledString` output, custom formatters) lives in `Roslyn/Formatting/`, which
started life as Roslyn's `CommonObjectFormatter` but has since been rewritten.

When updating: don't re-sync wholesale from upstream — that would clobber the
trimming. Diff against upstream at the paths matching each directory's namespace,
and expect the local copy to be a subset.
