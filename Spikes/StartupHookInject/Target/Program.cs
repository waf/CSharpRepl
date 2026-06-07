// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Diagnostics;

namespace Target;

/// <summary>An ordinary app with mutable static state. It knows nothing about the inspector.</summary>
public static class Program
{
    public static int Counter;     // the injected inspector reads this live
    public static int WriteProbe;  // the injected inspector writes this; we verify below

    public static void Main()
    {
        Console.WriteLine($"[target] Main starting (pid {Environment.ProcessId})");
        Counter = 1;

        // Mutate live state for a while so the inspector can observe it changing; meanwhile wait
        // for the inspector to write WriteProbe (up to a generous timeout to absorb Roslyn warmup).
        var sw = Stopwatch.StartNew();
        while (WriteProbe == 0 && sw.Elapsed < TimeSpan.FromSeconds(20))
        {
            Counter++;
            Thread.Sleep(200);
        }

        Console.WriteLine($"[target] Main finished. Counter={Counter}, WriteProbe={WriteProbe}");
        Console.WriteLine(WriteProbe == 9999
            ? "[target] PASS: the injected inspector wrote our real static (WriteProbe == 9999)"
            : "[target] FAIL: WriteProbe was never written by the inspector");
    }
}
