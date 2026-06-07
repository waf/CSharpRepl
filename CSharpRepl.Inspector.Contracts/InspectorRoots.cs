// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;

namespace CSharpRepl.Inspector.Contracts;

/// <summary>
/// Static holder for the captured "roots" of the target process. Lives in the default ALC (shared back
/// into the EngineALC) so both the bootstrap's capture code and the engine's submissions see the same
/// instance. In M2 this is populated with the target's root <see cref="IServiceProvider"/> (ASP.NET
/// <c>IStartupFilter</c> or Generic Host <c>DiagnosticListener</c>); until then it is null and only
/// statics/framework code are reachable.
/// </summary>
public static class InspectorRoots
{
    /// <summary>The target's captured root service provider, or null if none was captured.</summary>
    public static IServiceProvider? Services;
}
