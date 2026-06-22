// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Loader;

namespace CSharpRepl.Tests;

/// <summary>
/// Test-only diagnostic for the "one runtime instance per assembly" invariant (the #355 type-identity split).
/// Turns the manual diagnostic in <c>AssemblyReferenceReadme.md</c> §8 into something tests and spikes can assert on:
/// it groups every assembly loaded into the process - across <em>all</em> <see cref="AssemblyLoadContext"/>s - by
/// simple name. More than one instance under the same name (different version, ALC, or physical image) is a split.
/// </summary>
internal static class LoadedAssemblyInspector
{
    public readonly record struct Instance(string Name, Version? Version, string LoadContext, int HashCode, string Location)
    {
        public override string ToString() => $"v{Version} ALC='{LoadContext}' hash={HashCode} {Location}";
    }

    /// <summary>
    /// Snapshots every loaded assembly (from every load context) grouped by simple name. <paramref name="nameFilter"/>
    /// narrows to the closure under test (e.g. the EF Core / Npgsql family) so unrelated framework assemblies don't drown the signal.
    /// </summary>
    public static IReadOnlyDictionary<string, IReadOnlyList<Instance>> SnapshotByName(Func<string, bool>? nameFilter = null)
    {
        // AppDomain.GetAssemblies() spans all ALCs in .NET, but union with AssemblyLoadContext.All to be certain
        // nothing in a side context is missed, then de-duplicate by reference.
        var assemblies = AppDomain.CurrentDomain.GetAssemblies()
            .Concat(AssemblyLoadContext.All.SelectMany(alc => alc.Assemblies))
            .Distinct();

        return assemblies
            .Where(a => !a.IsDynamic)
            .Select(a =>
            {
                var name = a.GetName();
                return new Instance(
                    Name: name.Name ?? "<null>",
                    Version: name.Version,
                    LoadContext: AssemblyLoadContext.GetLoadContext(a)?.Name ?? "<unknown>",
                    HashCode: a.GetHashCode(),
                    Location: SafeLocation(a));
            })
            .Where(i => nameFilter is null || nameFilter(i.Name))
            .GroupBy(i => i.Name)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<Instance>)g.Distinct().ToList());
    }

    /// <summary>
    /// Names that appear more than once (the splits). Empty when the single-instance invariant holds.
    /// </summary>
    public static IReadOnlyList<KeyValuePair<string, IReadOnlyList<Instance>>> FindSplits(Func<string, bool>? nameFilter = null)
        => SnapshotByName(nameFilter).Where(kvp => kvp.Value.Count > 1).ToList();

    /// <summary>
    /// A human-readable report of the snapshot, used in assertion failure messages.
    /// </summary>
    public static string Describe(Func<string, bool>? nameFilter = null)
    {
        var snapshot = SnapshotByName(nameFilter);
        if (snapshot.Count == 0)
        {
            return "(no matching assemblies loaded)";
        }
        return string.Join(
            Environment.NewLine,
            snapshot.OrderBy(kvp => kvp.Key).Select(kvp =>
                $"{kvp.Key} ({kvp.Value.Count}):" + Environment.NewLine +
                string.Join(Environment.NewLine, kvp.Value.Select(i => "    " + i))));
    }

    private static string SafeLocation(System.Reflection.Assembly assembly)
    {
        try { return assembly.Location; }
        catch { return "<no location>"; }
    }
}
