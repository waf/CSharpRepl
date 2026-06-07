// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Spike.TargetLib;

/// <summary>A live, mutable object owned by the "target". The whole spike hinges on a submission
/// (compiled by Roslyn inside the isolated EngineALC) mutating THIS instance, visible from the default ALC.</summary>
public sealed class Counter
{
    public int Count;
    public void Inc() => Count++;
    public override string ToString() => $"Counter(Count={Count})";
}
