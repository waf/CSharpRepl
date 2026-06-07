// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Diagnostics;
using System.Threading;

namespace CSharpRepl.InjectedHook.TestTarget;

/// <summary>
/// An ordinary app with mutable static state. It knows nothing about the inspector — the integration
/// test injects the inspector via DOTNET_STARTUP_HOOKS and reads/writes this live state across the process
/// boundary. Stays alive for a bounded time so the test can connect and evaluate; the test kills it when done.
/// </summary>
public static class Program
{
    /// <summary>Climbs over time so the inspector can observe live, changing state.</summary>
    public static int Counter;

    /// <summary>Written by the inspector and re-read to prove cross-process writes land on the real static.</summary>
    public static int WriteProbe;

    /// <summary>A live object instance for REPL-parity tests (bind a var to it, reuse across submissions).</summary>
    public static readonly Service Shared = new();

    public static void Main()
    {
        // A generous self-timeout so a crashed/abandoned test run can't leave an orphan process forever.
        var stopwatch = Stopwatch.StartNew();
        while (stopwatch.Elapsed < TimeSpan.FromSeconds(120))
        {
            Counter++;
            Thread.Sleep(100);
        }
    }
}

/// <summary>A small domain type the inspector binds a local to, to prove cross-submission parity.</summary>
public sealed class Service
{
    public int Value { get; set; } = 41;

    public int Next() => ++Value;
}
