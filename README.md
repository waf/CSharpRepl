# C# REPL

A cross-platform command line <a href="https://en.wikipedia.org/wiki/Read%E2%80%93eval%E2%80%93print_loop" target="_blank"><abbr title="Read Eval Print Loop">REPL</abbr></a> for the rapid experimentation and exploration of C#. It supports intellisense, installing NuGet packages, and referencing local .NET projects and assemblies.

<div align="center">
  <a href="https://raw.githubusercontent.com/waf/CSharpRepl/main/.github/readme_assets/csharprepl.webp">
    <img src="https://raw.githubusercontent.com/waf/CSharpRepl/main/.github/readme_assets/csharprepl.png" alt="C# REPL screenshot" style="max-width:80%;">
  </a>
  <p align="center"><i>(click to view animation)</i></p>
</div>

C# REPL provides the following features:

- Syntax highlighting via ANSI escape sequences
- Intellisense with fly-out documentation
- Nuget package installation
- Reference local assemblies, solutions, and projects
- Navigate to source via Source Link
- IL disassembly (both Debug and Release mode)
- Fast and flicker-free rendering. A "diff" algorithm is used to only render what's changed.

## Installation

C# REPL is a .NET 5 global tool, and runs on Windows 10, Mac OS, and Linux. It can be installed via:

```console
dotnet tool install -g csharprepl
```

If you're running on Mac OS Catalina (10.15) or later, make sure you follow any additional directions printed to the screen. You may to update your PATH variable in order to use .NET global tools.

After installation is complete, run `csharprepl` to begin. C# REPL can be updated via `dotnet tool update -g csharprepl`.

## Usage:

Run `csharprepl` from the command line to begin an interactive session. The default colorscheme uses the color palette defined by your terminal, but these colors can be changed using a [`theme.json`](https://github.com/waf/CSharpRepl/blob/main/CSharpRepl/themes/dracula.json) file provided as a command line argument.

### Evaluating Code

Type some C# into the prompt and press <kbd>Enter</kbd> to run it. The result, if any, will be printed:

```csharp
> Console.WriteLine("Hello World")
Hello World

> DateTime.Now.AddDays(8)
[6/7/2021 5:13:00 PM]
```

To evaluate multiple lines of code, use <kbd>Shift+Enter</kbd> to insert a newline:

```csharp
> var x = 5;
  var y = 8;
  x * y
40
```

Additionally, if the statement is not a "complete statement" a newline will automatically be inserted when <kbd>Enter</kbd> is pressed. For example, in the below code, the first line is not a syntactically complete statement, so when we press enter we'll go down to a new line:

```csharp
> if (x == 5)
  | // caret position, after we press Enter on Line 1
```

Finally, pressing <kbd>Ctrl+Enter</kbd> will show a "detailed view" of the result. For example, for the `DateTime.Now` expression below, on the first line we pressed <kbd>Enter</kbd>, and on the second line we pressed <kbd>Ctrl+Enter</kbd> to view more detailed output:

```csharp
> DateTime.Now // Pressing Enter shows a reasonable representation
[5/30/2021 5:13:00 PM]

> DateTime.Now // Pressing Ctrl+Enter shows a detailed representation
[5/30/2021 5:13:00 PM] {
  Date: [5/30/2021 12:00:00 AM],
  Day: 30,
  DayOfWeek: Sunday,
  DayOfYear: 150,
  Hour: 17,
  InternalKind: 9223372036854775808,
  InternalTicks: 637579915804530992,
  Kind: Local,
  Millisecond: 453,
  Minute: 13,
  Month: 5,
  Second: 0,
  Ticks: 637579915804530992,
  TimeOfDay: [17:13:00.4530992],
  Year: 2021,
  _dateData: 9860951952659306800
}
```

**A note on semicolons**: C# expressions do not require semicolons, but [statements](https://stackoverflow.com/questions/19132/expression-versus-statement) do. If a statement is missing a required semicolon, a newline will be added instead of trying to run the syntatically incomplete statement; simply type the semicolon to complete the statement.

```csharp
> var now = DateTime.Now; // assignment statement, semicolon required

> DateTime.Now.AddDays(8) // expression, we don't need a semicolon
[6/7/2021 5:03:05 PM]
```

### Keyboard Shortcuts

- **Basic Usage**
  - <kbd>Ctrl+C</kbd> - Cancel current line
  - <kbd>Ctrl+L</kbd> - Clear screen
  - <kbd>Enter</kbd> - Evaluate the current line if it's a syntactically complete statement; otherwise add a newline
  - <kbd>Control+Enter</kbd> - Evaluate the current line, and return a more detailed representation of the result
  - <kbd>Shift+Enter</kbd> - Insert a new line (this does not currently work on Linux or Mac OS; Hopefully this will work in .NET 7)
  - <kbd>Ctrl+Shift+C</kbd> - Copy current line to clipboard
  - <kbd>Ctrl+V</kbd>, <kbd>Shift+Insert</kbd>, and <kbd>Ctrl+Shift+V</kbd> - Paste text to prompt. Automatically trims leading indent
- **Code Actions**
  - <kbd>F1</kbd> - Opens the MSDN documentation for the class/method under the caret ([example](https://docs.microsoft.com/en-US/dotnet/api/System.DateTime.AddDays?view=net-5.0))
  - <kbd>F9</kbd> - Shows the IL (intermediate language) for the current statement in Debug mode. 
  - <kbd>Ctrl+F9</kbd> - Shows the IL for the current statement with Release mode optimizations.
  - <kbd>F12</kbd> - Opens the source code in the browser for the class/method under the caret, if the assembly supports [Source Link](https://github.com/dotnet/sourcelink).
- **Autocompletion**
  - <kbd>Ctrl+Space</kbd> - Open autocomplete menu. If there's a single option, pressing <kbd>Ctrl+Space</kbd> again will select the option
  - <kbd>Enter</kbd>, <kbd>Right Arrow</kbd>, <kbd>Tab</kbd> - Select active autocompletion option
  - <kbd>Escape</kbd> - closes autocomplete menu
- **Text Navigation**
  - <kbd>Home</kbd> and <kbd>End</kbd> - Navigate to beginning of a single line and end of a single line, respectively
  - <kbd>Ctrl+Home</kbd> and <kbd>Ctrl+End</kbd> - Navigate to beginning of line and end across multiple lines in a multiline prompt, respectively
  - <kbd>Arrows</kbd> - Navigate characters within text
  - <kbd>Ctrl+Arrows</kbd> - Navigate words within text
  - <kbd>Ctrl+Backspace</kbd> - Delete previous word
  - <kbd>Ctrl+Delete</kbd> - Delete next word

### Adding References

Use the `#r` command to add assembly or nuget references.

- For assembly references, run `#r "AssemblyName"` or `#r "path/to/assembly.dll"`
- For project references, run `#r "path/to/project.csproj"`. Solution files (.sln) can also be referenced.
- For nuget references, run `#r "nuget: PackageName"` to install the latest version of a package, or `#r "nuget: PackageName, 13.0.5"` to install a specific version (13.0.5 in this case).

<p align="center">
  <img src="https://raw.githubusercontent.com/waf/CSharpRepl/main/.github/readme_assets/nuget.png" alt="Installing nuget packages" style="max-width:80%;">
</p>

To run ASP.NET applications inside the REPL, start the `csharprepl ` application with the `--framework` parameter, specifying the `Microsoft.AspNetCore.App` shared framework. Then, use the above `#r` command to reference the application DLL. See the *Command Line Configuration* section below for more details.

```console
csharprepl --framework  Microsoft.AspNetCore.App
```

## Command Line Configuration

The C# REPL supports multiple configuration flags to control startup, behavior, and appearance:

```
csharprepl [OPTIONS] [response-file.rsp] [script-file.csx] [-- <additional-arguments>]
```

Supported options are:

- OPTIONS:
    - `-r <dll>` or `--reference <dll>`: Reference an assembly, project file, or nuget package. Can be specified multiple times. Uses the same syntax as `#r` statements inside the REPL. For example, `csharprepl -r "nuget:Newtonsoft.Json" "path/to/myproj.csproj"`
      - When an assembly or project is referenced, assemblies in the containing directory will be added to the assembly search path. This means that you don't need to manually add references to all of your assembly's dependencies (e.g. other references and nuget packages). Referencing the main entry assembly is enough.
    - `-u <namespace>` or `--using <namespace>`: Add a using statement. Can be specified multiple times.
    - `-f <framework>` or `--framework <framework>`: Reference a [shared framework](https://docs.microsoft.com/en-us/aspnet/core/fundamentals/metapackage-app). The available shared frameworks depends on the local .NET installation, and can be useful when running an ASP.NET application from the REPL. Example frameworks are:
        - Microsoft.NETCore.App (default)
        - Microsoft.AspNetCore.All
        - Microsoft.AspNetCore.App
        - Microsoft.WindowsDesktop.App
    - `-t <theme.json>` or `--theme <theme.json>`: Read a theme file for syntax highlighting. This theme file associates C# syntax classifications with colors. The color values can be full RGB, or ANSI color names (defined in your terminal's theme). The [NO_COLOR](https://no-color.org/) standard is supported.
    - `-v` or `--version`: Show version number and exit.
    - `-h` or `--help`: Show help and exit.
- `response-file.rsp`: A filepath of an .rsp file, containing any of the above command line options.
- `script-file.csx`: A filepath of a .csx file, containing lines of C# to evaluate before starting the REPL. Arguments to this script can be passed as `<additional-arguments>`, after a double hyphen (`--`), and will be available in a global `args` variable.

If you have [`dotnet-suggest`](https://github.com/dotnet/command-line-api/blob/main/docs/dotnet-suggest.md) enabled, all options can be tab-completed, including values provided to `--framework` and .NET namespaces provided to `--using`.

## Integrating with other software

C# REPL is a standalone software application, but it can be useful to integrate it with other developer tools:

### Windows Terminal

To add C# REPL as a menu entry in Windows Terminal, add the following profile to Windows Terminal's `settings.json` configuration file (under the JSON property `profiles.list`):

```json
{
    "name": "C# REPL",
    "commandline": "csharprepl"
},
```

To get the exact colors shown in the screenshots in this README, install the [Windows Terminal Dracula theme](https://github.com/dracula/windows-terminal).

### Visual Studio Code

To use the C# REPL with Visual Studio Code, simply run the `csharprepl` command in the Visual Studio Code terminal. To send commands to the REPL, use the built-in `Terminal: Run Selected Text In Active Terminal` command from the Command Palette (`workbench.action.terminal.runSelectedText`).

<p align="center">
  <img src="https://raw.githubusercontent.com/waf/CSharpRepl/main/.github/readme_assets/vscode.png" alt="Visual Studio Code screenshot" style="max-width:90%;">
</p>


### Windows OS

To add the C# REPL to the Windows Start Menu for quick access, you can run the following PowerShell command, which will start C# REPL in Windows Terminal:

```powershell
$shell = New-Object -ComObject WScript.Shell
$shortcut = $shell.CreateShortcut("$env:appdata\Microsoft\Windows\Start Menu\Programs\csharprepl.lnk")
$shortcut.TargetPath = "wt.exe"
$shortcut.Arguments = "-w 0 nt csharprepl.exe"
$shortcut.Save()
```

You may also wish to add a shorter alias for C# REPL, which can be done by creating a `.cmd` file somewhere on your path. For example, put the following contents in `C:\Users\username\.dotnet\tools\csr.cmd`:

```shell
wt -w 0 nt csharprepl
```

This will allow you to launch C# REPL by running `csr` from anywhere that accepts Windows commands, like the Window Run dialog.

## Comparison with other REPLs

This project is far from being the first REPL for C#. Here are some other projects; if this project doesn't suit you, another one might!

**Visual Studio's C# Interactive pane** is full-featured (it has syntax highlighting and intellisense) and is part of Visual Studio. This deep integration with Visual Studio is both a benefit from a workflow perspective, and a drawback as it's not cross-platform. As far as I know, the C# Interactive pane does not support NuGet packages or navigating to documentation/source code. Subjectively, it does not follow typical command line keybindings, so can feel a bit foreign.

**csi.exe** ships with C# and is a command line REPL. It's great because it's a cross platform REPL that comes out of the box, but it doesn't support syntax highlighting or autocompletion.

**dotnet script** allows you to run C# scripts from the command line. It has a REPL built-in, but the predominant focus seems to be as a script runner. It's a great tool, though, and has a strong community following.

**dotnet interactive** is a tool from Microsoft that creates a Jupyter notebook for C#, runnable through Visual Studio Code. It also provides a general framework useful for running REPLs.

## Contributing

If you'd like to help out, thanks! We use Visual Studio 2022 for development, though any standard .NET 5 development environment should work. Please read through these guidelines to get started:

- Read through the [ARCHITECTURE.md](/ARCHITECTURE.md) file to understand how csharprepl works. Depending on what you want to do, changes to the underlying PrettyPrompt library may be required.
- For new features, please open an issue first to discuss and design the feature. This will help reduce the chance of conflicting designs.
- Please include an xunit test, and ensure any code warnings are resolved.

Thanks!
