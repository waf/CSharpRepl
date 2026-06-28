## Unreleased

- **Breaking:** the "inspect a running process" command has been renamed from `inspect` to `connect`, to avoid confusion with the `dotnet-inspect` tool. `csharprepl inspect <pid>`, `inspect init`, and `inspect list` are now `csharprepl connect <pid>`, `connect init`, and `connect list`. Relaunch your target with the env vars from the new `connect init` (the startup-hook payload moved from the `inspector/` directory to `connector/`).

## Release 0.9.1

- Simple variable declarations no longer require a trailing semicolon: typing `int i = 0` and pressing <kbd>Enter</kbd> now auto-inserts a semicolon ([#499](https://github.com/waf/CSharpRepl/pull/499)).
- Improved NuGet installation output, using a Spectre.Console status display with log lines rendered above it ([#500](https://github.com/waf/CSharpRepl/pull/500)).
- Autocomplete menu: replaced the colored unicode circles/squares with more meaningful characters like Ⓕ for fields and Ⓟ for properties, ([#503](https://github.com/waf/CSharpRepl/pull/503)).
- Reworked assembly and NuGet loading so that all references load through a single, dedicated `AssemblyLoadContext`. Fixes a class of assembly and nuget loading errors ([#498](https://github.com/waf/CSharpRepl/pull/498)).
- AI code-completion errors (for example network or HTTP failures) are now caught and surfaced as REPL feedback instead of crashing the REPL ([#501](https://github.com/waf/CSharpRepl/pull/501)).

## Release 0.9.0

- Live method replacement when inspecting a running process: replace a live function in the target application with a REPL-defined function ([#493](https://github.com/waf/CSharpRepl/pull/493)).
  - `#replace <method> with <replFunction>` replaces the original method
  - `#wrap <method> with <replFunction>` wraps the original method (the replacement's first parameter is an `orig` delegate that calls the original),
  - `#patches` lists active patches
  - `#revert <id>`/`#revert all` undoes them. Instance methods take the instance as their first parameter; patches take effect immediately and persist in the target until reverted or the process exits.
  - Generic methods and pointer parameters are not supported, as well as call sites the JIT already inlined.
- Typing a built-in command (`exit`, `clear`, `help`) in full now submits it immediately, instead of requiring a second <kbd>Enter</kbd> to first accept the completion-menu item and then submit ([#495](https://github.com/waf/CSharpRepl/pull/495)).
- **Breaking:** the AI code-completion feature (<kbd>Ctrl+Alt+Space</kbd>) is now provider-agnostic. The OpenAI-specific options (`--openAIApiKey`, `--openAIPrompt`, `--openAIModel`, `--openAIHistoryCount`) have been removed and replaced with provider-neutral options: `--aiApiKey`, `--aiPrompt`, `--aiModel`, and `--aiHistoryCount` ([#494](https://github.com/waf/CSharpRepl/pull/494)).
  - `--aiProvider` selects a built-in preset (`openai` (default), `anthropic`, `grok`, `deepseek`, `gemini`, `mistral`, `codestral`), which includes a default endpoint, model, and API-key environment variable (`OPENAI_API_KEY`, `ANTHROPIC_API_KEY`, `XAI_API_KEY`, `DEEPSEEK_API_KEY`, `GEMINI_API_KEY`, `MISTRAL_API_KEY`, `CODESTRAL_API_KEY`). Point `--aiEndpoint` at any other OpenAI-compatible API to use a provider that isn't listed.
  - The default OpenAI model is now `gpt-5.4-mini` (was `gpt-4o`).

## Release 0.8.1

- On Windows, new installations store the config file, prompt history, and NuGet/symbol caches in the local profile (`%LOCALAPPDATA%\.csharprepl`) instead of the roaming profile (`%APPDATA%`), so the package cache is no longer synchronized across machines.
  - Existing installations keep using their current (roaming) location; macOS and Linux are unaffected ([#391](https://github.com/waf/CSharpRepl/issues/391)).
- Support native assets in Nuget packages and improve assembly version conflict resolution (use highest) ([#483](https://github.com/waf/CSharpRepl/issues/483)).
- Add `csharprepl inspect list`, which lists the running, inspector-enabled processes you can attach to, so you no longer have to find the process id by hand ([#482](https://github.com/waf/CSharpRepl/issues/482)).
- Fix `inspect init` printing `cmd` syntax (`set "..."`) instead of PowerShell syntax when the RID-specific tool is run from PowerShell. The `.cmd` tool shim makes Windows insert a transient `cmd.exe /c` between PowerShell and the tool; shell detection now walks past such a cmd when its own parent is itself a recognized shell. ([#481](https://github.com/waf/CSharpRepl/pull/481)).
- Overload help is now filtered by access kind: static methods no longer appear when invoking on an instance (`value.M(`), and instance methods no longer appear when invoking on a type (`Type.M(`) ([#487](https://github.com/waf/CSharpRepl/pull/487)).
- A failing `Debug.Assert` in evaluated code now throws a catchable exception instead of crashing the REPL ([#374](https://github.com/waf/CSharpRepl/issues/374), [#486](https://github.com/waf/CSharpRepl/pull/486)).
- Improve the `--help` text for the key-binding options, documenting the key-pattern syntax and that an option can be passed multiple times to bind several keys to one action ([#485](https://github.com/waf/CSharpRepl/pull/485)).
- Dependency upgrades, including PrettyPrompt 6.0.2, which fixes a macOS/Linux issue where the prompt could render at a stale screen position until the next keypress ([#395](https://github.com/waf/CSharpRepl/issues/395), [#490](https://github.com/waf/CSharpRepl/pull/490)).

## Release 0.8.0

- New `inspect` feature: attach to a separate, already-running .NET process and evaluate C# inside it with full local-REPL parity (IntelliSense, highlighting, pretty-printing), reading and writing its live state. Run `csharprepl inspect init` to get the launch environment variables, then `csharprepl inspect <pid>`. It is cooperative and opt-in only ([#477](https://github.com/waf/CSharpRepl/pull/477)).
- Integrate ILSpy's lowering/decompilation feature, so you can see the lowered C# for a submission ([#471](https://github.com/waf/CSharpRepl/pull/471)).
- Intermediate Language (IL) syntax highlighting, with the original C# shown inline as comments ([#470](https://github.com/waf/CSharpRepl/pull/470)).
- Fix an assembly-resolve issue when loading ASP.NET Core ([#468](https://github.com/waf/CSharpRepl/pull/468)).
- Fix "System.Object is not defined or imported" error when referencing a `.csproj` ([#466](https://github.com/waf/CSharpRepl/pull/466)).
- Fix navigate-to-source index range to be inclusive ([#469](https://github.com/waf/CSharpRepl/pull/469)).
- Better handling for newlines and nested syntax-highlight spans ([#474](https://github.com/waf/CSharpRepl/pull/474)).
- Performance: remove duplicate assembly loads ([#475](https://github.com/waf/CSharpRepl/pull/475)).
- Dependency upgrades ([#472](https://github.com/waf/CSharpRepl/pull/472), [#479](https://github.com/waf/CSharpRepl/pull/479)).

## Release 0.7.1

- Fix syntax highlighting edge cases (emoji, new lines embedded in syntax highlighting spans e.g. raw string literals)

## Release 0.7.0

- Removed the noticeable lag on the first keystroke of a new session. Roslyn's editor services are now warmed up in a more effective order, and completion is briefly held back until that warm-up finishes so the first keystrokes stay responsive ([#459](https://github.com/waf/CSharpRepl/pull/459)).
- Fixed a performance issue where code completion slowed down and memory grew as a session accumulated submissions ([#459](https://github.com/waf/CSharpRepl/pull/459)).
- ReadyToRun platform-specific tool packages for faster warm-up ([#461](https://github.com/waf/CSharpRepl/pull/461)).

## Release 0.6.9

- Much better unicode/emoji support ([#458](https://github.com/waf/CSharpRepl/pull/458) - via an upgrade to PrettyPrompt 5.0).
- Add support for referencing `.slnx` solution files (thanks @gteijeiro!) ([#450](https://github.com/waf/CSharpRepl/pull/450)).
- Respect `packageSourceMapping` when installing NuGet packages ([#454](https://github.com/waf/CSharpRepl/pull/454)).
- Add extended descriptions for the `help`, `exit`, and `clear` commands (thanks @sugiiianaaa!) ([#449](https://github.com/waf/CSharpRepl/pull/449)).
- Add `--eval` and `--eval-file` flags for non-interactively evaluating C# code, printing the result, and exiting. AI agents can use this via a skill in the repository ([#457](https://github.com/waf/CSharpRepl/pull/457)).
- Dependency upgrades ([#454](https://github.com/waf/CSharpRepl/pull/454)).

## Release 0.6.8

- .NET 10 upgrade ([#424](https://github.com/waf/CSharpRepl/pull/424)).
- Better experience for built-in commands like help, exit, and clear ([#403](https://github.com/waf/CSharpRepl/pull/403)).
- Autoindentation fixes ([#389](https://github.com/waf/CSharpRepl/pull/389)).

## Release 0.6.7

- Add exception type name in error output panel ([#339](https://github.com/waf/CSharpRepl/pull/339)).
- Improved and colorized help output ([#338](https://github.com/waf/CSharpRepl/pull/338)).
- Fix navigate-to-source for generic types ([#342](https://github.com/waf/CSharpRepl/pull/342)).
- Handle exceptions from roslyn completion API ([#334](https://github.com/waf/CSharpRepl/pull/334)).
- Dependency upgrades ([#330](https://github.com/waf/CSharpRepl/pull/330), [#333](https://github.com/waf/CSharpRepl/pull/333), and [#349](https://github.com/waf/CSharpRepl/pull/349)).

## Release 0.6.6

- Upgrade to .NET 8
- Add a new `--culture` command line flag for launching CSharpRepl with a specific culture
- Improved pretty printing of generic types defined inside the CSharpRepl
- Dependency upgrades

## Release 0.6.5

- Upgrade PrettyPrompt library to get the following fixes:
    - Handle invalid history entries / history log corruption ([#267](https://github.com/waf/PrettyPrompt/pull/267)).

## Release 0.6.4

- Make help command show dynamic keybindings ([#289](https://github.com/waf/CSharpRepl/pull/289))
- Fix annoying completion commit triggers for dynamic variables and C# Range syntax ([#290](https://github.com/waf/CSharpRepl/pull/290))
- Minor NuGet upgrades and code cleanup ([#285](https://github.com/waf/CSharpRepl/pull/285) and [#291](https://github.com/waf/CSharpRepl/pull/291))
- Upgrade PrettyPrompt library to get the following fixes:
    - Better error messages on Linux when xsel is not installed ([#264](https://github.com/waf/PrettyPrompt/pull/264)).
    - Fix crash when Shift-Delete is pressed under certain conditions ([#263](https://github.com/waf/PrettyPrompt/pull/263)).
    - Add workaround for garbled utf-8 characters on Linux ([#261](https://github.com/waf/PrettyPrompt/pull/261)).

## Release 0.6.3

- If msbuild cannot be located, still allow basic REPL usage.
- Nuget installation - Handle multiple nuspecs that differ only by case.

## Release 0.6.2

- Fix handling of AltGr characters (e.g. typing `{` on AZERTY keyboards)
- Dependency updates and nullable reference warning cleanup
- Improve help text on smaller terminal widths

## Release 0.6.1

- Nuget package upgrade of underlying PrettyPrompt library

## Release 0.6.0

- Much improved output formatting. Supports a much more graphical dump of a wide range of objects, as well as syntax highlighting. Uses the excellent Spectre.Console library.
- Better exception formatting, featuring both a compact default format and a syntax highlighted verbose format.
- OpenAI autocompletions (requires an API key, which is pay-as-you-go)
- Intellisense support when files are executed via `#load`
- Better error message when .NET is installed to non-standard location but DOTNET_ROOT is not set
- Bugfix for nuget loading issue when referencing csproj and sln files
- Bugfix for nuget package installation when the package has implicit minor/patch versions
- Fix crash in the disassembler feature
- Fix crash in item completion logic (e.g. in `System.Threading.Mutex(`)

## Release 0.5.1

- Update Nuget package libraries to work on .NET 7
- Ensure the .NET 7 version of nuget libraries are installed if they're available, rather than e.g. falling back to .NET Standard

## Release 0.5.0

- Targets .NET 7
- Syntax highlighting and formatting for output
- Default to allowing C# Preview features
- Support referencing projects that target multiple frameworks
- Intelligent handling of the display of null literals vs code that returns void
- Update of PrettyPrompt library to fix crash related to completion pane sizing

## Release 0.4.0

- Visual Studio dark theme as default
- Many improvements around autocompletion menu usability
- New menu for navigating overloads menu
- Add configuration file and --configure command line switch
- Smart indentation for multiline statements
- Auto formatting of input
- When showing IL code, use more targeted disassembly output for simple statements
- Bugfixes for assembly, framework, and CSX loading
- Configurable keybindings
- Support loading of prerelease nuget packages and nuspec file fixes
- UTF-8 mode with autocompletion menu glyphs for differentiating between methods, types, properties, events, and delegates
- Reference all projects when a solution is referenced
- Formatted / colored help output
- Nuget dependency updates

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
