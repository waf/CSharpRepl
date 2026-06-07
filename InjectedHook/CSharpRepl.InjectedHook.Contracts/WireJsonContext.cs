// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Text.Json.Serialization;

namespace CSharpRepl.InjectedHook.Contracts;

/// <summary>
/// Source-generated <see cref="JsonSerializerContext"/> for the wire protocol. Serializing the polymorphic
/// <see cref="WireMessage"/> base type drives the <c>$kind</c> discriminator; the generator follows the type
/// graph from there (the derived messages, <see cref="RemoteValue"/>'s recursive members/items,
/// <see cref="RemoteException"/>, and the enums — whose string forms use the AOT-friendly
/// <c>JsonStringEnumConverter&lt;T&gt;</c>). Using source generation keeps the contracts assembly free of
/// reflection-based (de)serialization — no <c>Reflection.Emit</c> in the target process the hook is injected
/// into, cheaper first use, and trim/AOT-safety.
/// </summary>
[JsonSourceGenerationOptions(DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(WireMessage))]
internal sealed partial class WireJsonContext : JsonSerializerContext
{
}
