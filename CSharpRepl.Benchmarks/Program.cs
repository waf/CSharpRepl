// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using BenchmarkDotNet.Running;

// With no args, run every benchmark non-interactively. Pass BenchmarkDotNet filters to scope a run, e.g.
//   dotnet run -c Release --project CSharpRepl.Benchmarks -- --filter *Highlight*
var switcher = BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly);
if (args.Length == 0)
{
    switcher.RunAll();
}
else
{
    switcher.Run(args);
}

// Top-level statements generate a Program class; declaring it partial lets BenchmarkSwitcher reference typeof(Program).
public partial class Program { }
