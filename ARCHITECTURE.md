# Architecture

This document describes the design of csharprepl at a high level. It's useful if you want to contribute to csharprepl and are trying to understand how the code is organized and what the various responsibilities are.

Broadly, there are three main components to csharprepl's architecture:

1. [Roslyn libraries](https://github.com/dotnet/roslyn) - Written by Microsoft, these provide both high-level and low-level APIs for building, running, and analyzing C# code.
1. [PrettyPrompt library](https://github.com/waf/PrettyPrompt) - the PrettyPrompt library was written alongside csharprepl, and is packaged as a separate nuget package and repository. It's a Console.ReadLine replacement, and handles terminal features like accepting input, drawing autocompletion menus, syntax highlighting, keybindings, etc, and generally handles the "user experience" of the prompt. It's a general library, and does not know anything about C#; it instead provides callbacks that csharprepl implements to add C#-specific behavior.
1. csharprepl (this repository) - This application uses PrettyPrompt to collect input from the user, invokes the Roslyn APIs on that input, and then prints the result.

Each of the above parts is described in more detail below.

## Roslyn libraries

csharprepl uses two main areas of the Roslyn libraries:

- C# Scripting API ([docs](https://github.com/dotnet/roslyn/blob/b796152aff3a7f872bd70db26cc9f568bbdb14cc/docs/wiki/Scripting-API-Samples.md))
- C# Workspaces API ([docs](https://docs.microsoft.com/en-us/dotnet/csharp/roslyn-sdk/work-with-workspace))

The C# Scripting API allows for evaluating strings containing C# and returns the result. This is the core of the REPL experience; this core is quite simple, and a basic REPL could be implemented in a couple of lines of C# using this API. In csharprepl, this is in `ScriptRunner.cs`.

The C# Workspaces API provides all the ancillary editor features in csharprepl, like syntax highlighting, autocompletion, documentation tooltip information, etc. A workspace is conceptually similar to a Visual Studio solution, and it has multiple projects all contained in memory. Each line of a REPL is implemented as a single project with a single document, and each project has a reference to the previously submitted project. In other words, the workspace is a linked-list of projects, where each project is a line of the REPL. Submitting a new line in csharprepl will create a new project and document. Features like syntax highlighting and autocompletion read this document as their input.

These two APIs are quite separate; one of the main tasks of csharprepl is to ensure that when code is evaluated in the REPL, both of these APIs receive consistent information (see `RoslynServices.cs`).

## PrettyPrompt library

This prompt library is initialized/called in the main `Program.cs`, in the "Read" part of the "Read-Eval-Print-Loop." It's instantiated with callback functions to handle syntax highlighting, autocompletion, and more; these callbacks invoke the above `RoslynServices.cs` class to fulfill the callbacks. The `PromptAdapter` class maps between Roslyn concepts (e.g. a span of text with the syntax classification) and the PrettyPrompt concepts (e.g. a span of text with a syntax highlight color).

## csharprepl

As previously described, csharprepl serves as an intermediary between the above two libraries. There are four main tasks that csharprepl performs:

- `Program.cs` handles basic features like command line arguments, help documentation, and the core read-eval-print-loop.
- Initialization logic is managed in `RoslynServices.cs`. The Roslyn libraries do a lot of heavy lifting and are relatively slow to initialize; RoslynServices serves as an entry point to all Roslyn services, and it manages this initialization in the background to keep csharprepl snappy. It ensures that when the Roslyn services are called, they either don't block, or they wait for initialization to complete if required.
    - You can see this in action by starting csharprepl and typing some C# code really quickly before initialization can fully complete. You'll be able to type without delay as it won't block, but you might see syntax highlighting kick in a few seconds later, after initialization is complete; this is what RoslynServices is managing.
- Synchronization between the C# Scripting API and C# Workspaces API, as described in *Roslyn libraries* above, is also managed by `RoslynServices.cs`. Upon successful evaluation of a script, it updates the workspaces API with a new project and updates the active document to use for syntax highlighting, autocompletion, and more.
- Assembly reference management - Management of [Shared Frameworks](https://natemcmaster.com/blog/2017/12/21/netcore-primitives/), [implementation vs reference assemblies](https://docs.microsoft.com/en-us/dotnet/standard/assembly/reference-assemblies), and adding references dynamically are handled in the `AssemblyReferenceService.cs`. This class is called by our MetadataReferenceResolver implementation, which is an extension point provided by Roslyn.
    - This MetadataReferenceResolver is responsible for evaluating `#r` statements, and delegates for assembly references, nuget package installation, csproj/sln references, and shared framework loading. The implementation is in `CompositeMetadataReferenceResolver.cs`
