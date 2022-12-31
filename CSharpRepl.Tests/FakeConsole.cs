#region License Header
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.
#endregion

#nullable enable

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using CSharpRepl.Services;
using NSubstitute;
using NSubstitute.Core;
using PrettyPrompt;
using PrettyPrompt.Consoles;
using Spectre.Console;
using Spectre.Console.Rendering;
using Spectre.Console.Testing;

namespace CSharpRepl.Tests;

internal static class FakeConsole
{
    private static readonly Regex FormatStringSplit = new(@"({\d+}|{{|}}|.)", RegexOptions.Compiled);

    public static (IConsoleEx console, StringBuilder stdout, StringBuilder stderr) CreateStubbedOutputAndError(int width = 100, int height = 100)
    {
        var stub = Create(width, height);
        var stdout = new StringBuilder();
        var stderr = new StringBuilder();
        stub.When(c => c.Write(Arg.Any<string>())).Do(args => stdout.Append(args.Arg<string>()));
        stub.When(c => c.WriteLine(Arg.Any<string>())).Do(args => stdout.AppendLine(args.Arg<string>()));
        stub.When(c => c.WriteError(Arg.Any<string>())).Do(args => stderr.Append(args.Arg<string>()));
        stub.When(c => c.WriteErrorLine(Arg.Any<string>())).Do(args => stderr.AppendLine(args.Arg<string>()));
        return (stub, stdout, stderr);
    }

    public static (IConsoleEx console, StringBuilder stdout) CreateStubbedOutput(int width = 100, int height = 100)
    {
        var console = Create(width, height);
        var stdout = new StringBuilder();
        console.When(c => c.Write(Arg.Any<string>())).Do(args => stdout.Append(args.Arg<string>()));
        console.When(c => c.WriteLine(Arg.Any<string>())).Do(args => stdout.AppendLine(args.Arg<string>()));
        return (console, stdout);
    }

    public static IConsoleEx Create(int width = 100, int height = 100)
    {
        var console = Substitute.For<FakeConsoleAbstract>();
        console.BufferWidth.Returns(width);
        console.WindowHeight.Returns(height);
        return console;
    }

    public static IReadOnlyList<string> GetAllOutput(this IConsoleEx consoleStub) =>
        consoleStub.ReceivedCalls()
            .Where(call => call.GetMethodInfo().Name == nameof(Console.Write))
            .Select(call =>
            {
                var arg = (string?)call.GetArguments().Single();
                Debug.Assert(arg != null);
                return arg;
            })
            .ToArray();

    public static string GetFinalOutput(this IConsoleEx consoleStub)
    {
        return consoleStub.GetAllOutput()[^2]; // second to last. The last is always the newline drawn after the prompt is submitted
    }

    /// <summary>
    /// Stub Console.ReadKey to return a series of keystrokes (<see cref="ConsoleKeyInfo" />).
    /// Keystrokes are specified as a <see cref="FormattableString"/> with any special keys,
    /// like modifiers or navigation keys, represented as FormattableString arguments (of type
    /// <see cref="ConsoleModifiers"/> or <see cref="ConsoleKey"/>).
    /// </summary>
    /// <example>$"{Control}LHello{Enter}" is turned into Ctrl-L, H, e, l, l, o, Enter key</example>
    public static ConfiguredCall StubInput(this IConsoleEx consoleStub, params FormattableString[] inputs)
    {
        var keys = inputs
            .SelectMany(line => MapToConsoleKeyPresses(line))
            .ToList();

        return consoleStub.StubInput(keys);
    }

    /// <summary>
    /// Stub Console.ReadKey to return a series of keystrokes (<see cref="ConsoleKeyInfo" />).
    /// Keystrokes are specified as a <see cref="FormattableString"/> with any special keys,
    /// like modifiers or navigation keys, represented as FormattableString arguments (of type
    /// <see cref="ConsoleModifiers"/> or <see cref="ConsoleKey"/>) and with optional Action to be invoked after key press.
    /// Use <see cref="Input(FormattableString)" and <see cref="Input(FormattableString, Action)"/> methods to create inputs./>
    /// </summary>
    public static ConfiguredCall StubInput(this IConsoleEx consoleStub, params FormattableStringWithAction[] inputs)
    {
        var keys = inputs
            .SelectMany(EnumerateKeys)
            .ToList();

        return consoleStub
            .ReadKey(intercept: true)
            .Returns(keys.First(), keys.Skip(1).ToArray());

        IEnumerable<Func<CallInfo, ConsoleKeyInfo>> EnumerateKeys(FormattableStringWithAction input)
        {
            var keyPresses = MapToConsoleKeyPresses(input.Input);
            if (keyPresses.Count > 0)
            {
                for (int i = 0; i < keyPresses.Count - 1; i++)
                {
                    int index = i; //copy for closure (important!)
                    yield return _ => keyPresses[index];
                }
                yield return _ =>
                {
                    input.ActionAfter?.Invoke();
                    return keyPresses[^1];
                };
            }
            else if (input.ActionAfter != null)
            {
                throw new InvalidOperationException("you can specify 'actionAfter' only after keyPress");
            }
        }
    }

    public static ConfiguredCall StubInput(this IConsoleEx consoleStub, List<ConsoleKeyInfo> keys)
    {
        return consoleStub
            .ReadKey(intercept: true)
            .Returns(keys.First(), keys.Skip(1).ToArray());
    }

    private static List<ConsoleKeyInfo> MapToConsoleKeyPresses(FormattableString input)
    {
        ConsoleModifiers modifiersPressed = 0;
        // split the formattable strings into a mix of format placeholders (e.g. {0}, {1}) and literal characters.
        // For the format placeholders, we can get the arguments as their original objects (ConsoleModifiers or ConsoleKey).
        return FormatStringSplit
            .Matches(input.Format)
            .Aggregate(
                seed: new List<ConsoleKeyInfo>(),
                func: (list, key) =>
                {
                    bool isPlaceholder = key.Value.StartsWith('{') && key.Value.EndsWith('}');
                    bool isEscapedBrace = key.Value == "{{" || key.Value == "}}";

                    if (isPlaceholder)
                    {
                        var formatArgument = input.GetArgument(int.Parse(key.Value.Trim('{', '}')));
                        modifiersPressed = AppendFormatStringArgument(list, key, modifiersPressed, formatArgument);
                    }
                    else if (isEscapedBrace)
                    {
                        modifiersPressed = AppendLiteralKey(list, key.Value.First(), modifiersPressed);
                    }
                    else
                    {
                        modifiersPressed = AppendLiteralKey(list, key.Value.Single(), modifiersPressed);
                    }

                    return list;
                }
            );
    }

    private static ConsoleModifiers AppendLiteralKey(List<ConsoleKeyInfo> list, char keyChar, ConsoleModifiers modifiersPressed)
    {
        list.Add(CharToConsoleKey(keyChar).ToKeyInfo(keyChar, modifiersPressed));
        return 0;
    }

    public static ConsoleKey CharToConsoleKey(char keyChar) =>
        keyChar switch
        {
            '.' => ConsoleKey.OemPeriod,
            ',' => ConsoleKey.OemComma,
            '-' => ConsoleKey.OemMinus,
            '+' => ConsoleKey.OemPlus,
            '\'' => ConsoleKey.Oem7,
            '/' => ConsoleKey.Divide,
            '!' => ConsoleKey.D1,
            '@' => ConsoleKey.D2,
            '#' => ConsoleKey.D3,
            '$' => ConsoleKey.D4,
            '%' => ConsoleKey.D5,
            '^' => ConsoleKey.D6,
            '&' => ConsoleKey.D7,
            '*' => ConsoleKey.D8,
            '(' => ConsoleKey.D9,
            ')' => ConsoleKey.D0,
            <= (char)255 => (ConsoleKey)char.ToUpper(keyChar),
            _ => ConsoleKey.Oem1
        };

    private static ConsoleModifiers AppendFormatStringArgument(List<ConsoleKeyInfo> list, Match key, ConsoleModifiers modifiersPressed, object? formatArgument)
    {
        switch (formatArgument)
        {
            case ConsoleModifiers modifier:
                return modifiersPressed | modifier;
            case ConsoleKey consoleKey:
                var parsed = char.TryParse(key.Value, out char character);
                list.Add(consoleKey.ToKeyInfo(parsed ? character : MapSpecialKey(consoleKey), modifiersPressed));
                return 0;
            case char c:
                list.Add(CharToConsoleKey(c).ToKeyInfo(c, modifiersPressed));
                return 0;
            case string text:
                if (text.Length > 0)
                {
                    list.Add(CharToConsoleKey(text[0]).ToKeyInfo(text[0], modifiersPressed));
                }
                for (int i = 1; i < text.Length; i++)
                {
                    list.Add(CharToConsoleKey(text[i]).ToKeyInfo(text[i], 0));
                }
                return 0;
            default: throw new ArgumentException("Unknown value: " + formatArgument, nameof(formatArgument));
        }
    }

    private static char MapSpecialKey(ConsoleKey consoleKey) =>
        consoleKey switch
        {
            ConsoleKey.Backspace => '\b',
            ConsoleKey.Tab => '\t',
            ConsoleKey.Oem7 => '\'',
            ConsoleKey.Spacebar => ' ',
            _ => '\0' // home, enter, arrow keys, etc
        };

    public static FormattableStringWithAction Input(FormattableString input) => new(input);
    public static FormattableStringWithAction Input(FormattableString input, Action actionAfter) => new(input, actionAfter);

    public readonly struct FormattableStringWithAction
    {
        public readonly FormattableString Input;
        public readonly Action? ActionAfter;

        public FormattableStringWithAction(FormattableString input)
            : this(input, null) { }

        public FormattableStringWithAction(FormattableString input, Action? actionAfter)
        {
            Input = input;
            ActionAfter = actionAfter;
        }
    }
}

public abstract class FakeConsoleAbstract : IConsoleEx
{
    private readonly IAnsiConsole ansiConsole = new TestConsole();

    public abstract int CursorTop { get; }
    public abstract int BufferWidth { get; }
    public abstract int WindowHeight { get; }
    public abstract int WindowTop { get; }
    public abstract bool KeyAvailable { get; }
    public abstract bool IsErrorRedirected { get; }
    public abstract bool CaptureControlC { get; set; }

    public abstract event ConsoleCancelEventHandler CancelKeyPress;

    public abstract void Clear();
    public abstract void HideCursor();
    public abstract void InitVirtualTerminalProcessing();
    public abstract ConsoleKeyInfo ReadKey(bool intercept);
    public abstract void ShowCursor();

    public abstract void Write(string? value);
    public abstract void WriteError(string? value);
    public abstract void WriteErrorLine(string? value);
    public abstract void WriteLine(string? value);

    public abstract void WriteError(IRenderable renderable, string text);

    //following implementations is needed because NSubstitute does not support ROS
    public void Write(ReadOnlySpan<char> value) => Write(value.ToString());
    public void WriteError(ReadOnlySpan<char> value) => WriteError(value.ToString());
    public void WriteErrorLine(ReadOnlySpan<char> value) => WriteErrorLine(value.ToString());
    public void WriteLine(ReadOnlySpan<char> value) => WriteLine(value.ToString());

    //IAnsiConsole
    public Profile Profile => ansiConsole.Profile;
    public IAnsiConsoleCursor Cursor => ansiConsole.Cursor;
    public IAnsiConsoleInput Input => ansiConsole.Input;
    public IExclusivityMode ExclusivityMode => ansiConsole.ExclusivityMode;
    public RenderPipeline Pipeline => ansiConsole.Pipeline;
    public void Clear(bool home) => ansiConsole.Clear(home);
    public void Write(IRenderable renderable) => ansiConsole.Write(renderable);
}