// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Diagnostics;
using System.Threading;

namespace CSharpRepl.Services.Roslyn;

/// <summary>
/// Makes a failed <see cref="Debug.Assert"/> in evaluated code throw instead of crashing the REPL.
/// </summary>
internal static class DebugAssertHandler
{
    private static int installed;

    /// <summary>
    /// Installs the throwing listener process-wide. Only the first call takes effect.
    /// </summary>
    public static void Install()
    {
        if (Interlocked.Exchange(ref installed, 1) != 0)
        {
            return;
        }

        var listeners = Trace.Listeners;

        // Remove the default listener(s) - their Fail() is what FailFasts the process.
        for (var i = listeners.Count - 1; i >= 0; i--)
        {
            if (listeners[i] is DefaultTraceListener)
            {
                listeners.RemoveAt(i);
            }
        }

        listeners.Add(new ThrowingTraceListener());
    }

    private sealed class ThrowingTraceListener : TraceListener
    {
        // Debug.Write / Debug.WriteLine route here; the default listener forwarded them to the debugger (unused in the REPL), so swallowing them does not cause any harm.
        public override void Write(string? message) { }

        public override void WriteLine(string? message) { }

        public override void Fail(string? message) => throw Build(message, null);

        public override void Fail(string? message, string? detailMessage) =>
            throw Build(message, detailMessage);

        private static DebugAssertException Build(string? message, string? detailMessage)
        {
            var text = string.IsNullOrEmpty(message) ? "Assertion failed." : message;
            if (!string.IsNullOrEmpty(detailMessage))
            {
                text += Environment.NewLine + detailMessage;
            }
            return new DebugAssertException(text);
        }
    }
}

/// <summary>
/// Thrown when an assertion fails in evaluated code. We return/throw this which causes the REPL to show the error, instead of crashing.
/// </summary>
internal sealed class DebugAssertException : Exception
{
    public DebugAssertException(string message)
        : base(message) { }
}
