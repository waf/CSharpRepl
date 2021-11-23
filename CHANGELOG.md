## Release 0.3.5

- .NET 6 and C# 10 support
- Exit when user presses ctrl-d
- If the user presses ctrl-c when there's a long running (or infinite!) evaluation, exit the application. This allows a way of interrupting infinite or slow processes.
- Roslyn library upgrade
- Bugfix for NuGet packages that don't specify any dependency groups (for target frameworks)

## Release 0.3.4

- Add a --trace command line option for generating trace logs of CSharpRepl internals
- Support loading shared frameworks from the ~/.nuget directory.

## Release 0.3.3

- Now featuring dotnet-suggest support! If you've set up dotnet-suggest, you'll get excellent tab completion of command line parameters.
    - [How to set up dotnet-suggest](https://github.com/dotnet/command-line-api/blob/main/docs/dotnet-suggest.md)
    - [My blog post with more info on this feature](https://fuqua.io/blog/2021/09/enabling-command-line-completions-with-dotnet-suggest/)

## Release 0.3.2

Features:

- Press F12 to navigate to the source of a class/method/property. It uses source link to open the source in the browser.
- Press F9 to view IL code of a statement in Debug mode. Ctrl+F9 shows the IL when the code is compiled in Release mode.

## Release 0.3.1

Features:

- Fix sln/csproj building on non-windows platforms
- Enable roll-forward behavior to support cases where .NET 5 is not
installed (and .NET 6 preview is).

## Release 0.3.0

minor bugfix release

- Support Ctrl+Function keys on WSL2 / Windows Terminal
- Fix case where caching was too aggressive, and causing intellisense to
  not pick up new types that were imported (via a using statement) on
  the previous line

## Release 0.2.9

Contains PrettyPrompt library upgrade, to get a bug fix. This fixes a bug where the intellisense menu was auto-closed too aggressively.

## Release 0.2.8

- text selection with cut/copy/paste support
- undo/redo
- pressing "Up" to navigate history will filter history based on text in the prompt

## Release 0.2.7

- add `clear` command for clearing the screen (thanks @aixasz!)
- add IList<string> Args and Print command. This increases compatibility with other REPL's csx implementations
- improve in-application help text

## Release 0.2.6 and 0.2.5

- Bugfix releases for intellisense

## Release 0.2.4

- Support referencing csproj and sln files via `#r` statements and the `--reference` command line options.
- Add global `args` variable that represents command line arguments provided to csharprepl after a double hyphen (--)
Better document new features, add ARCHITECTURE.md

## Release 0.2.3

- Better full-width character support (mainly for CJK character support)
- Allow for relative paths in #r statements
- Add ctrl+enter behavior for strings; it shows the string unescaped (by
  default, strings are shown escaped).

## Release 0.2.2

- Improve help text (thanks @IBIT-ZEE)
- Support .NET 6 preview versions (thanks @PathogenDavid)

## Release 0.2.1

- Fix crash on certain inputs that cause cache key conflicts - thanks @IBIT-ZEE
- Pull in latest PrettyPrompt dependency, to get history deduplication - thanks @realivanjx
- Fix nuget package "Project Site" and "Source repository" URLs - thanks @zahirtezcan-bugs

## Release 0.2

First public release of CSharpRepl!
