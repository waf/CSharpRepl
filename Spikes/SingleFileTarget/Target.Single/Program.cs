// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Diagnostics;

namespace Target.Single;

public static class Program
{
    public static int Counter;
    public static int WriteProbe;

    public static void Main()
    {
        Console.WriteLine($"[target] single-file app starting (pid {Environment.ProcessId})");
        Console.WriteLine($"[target] entry assembly Location = '{typeof(Program).Assembly.Location}' (empty => bundled single-file)");

        Counter = 1;
        var sw = Stopwatch.StartNew();
        while (WriteProbe == 0 && sw.Elapsed < TimeSpan.FromSeconds(15))
        {
            Counter++;
            Thread.Sleep(200);
        }

        Console.WriteLine($"[target] finished. Counter={Counter}, WriteProbe={WriteProbe}");
        Console.WriteLine(WriteProbe == 9999
            ? "[target] PASS: the injected inspector wrote our static via reflection (single-file)"
            : "[target] (WriteProbe not set by inspector — see the degradation report above)");
    }
}
