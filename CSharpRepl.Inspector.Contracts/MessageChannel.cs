// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Buffers.Binary;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace CSharpRepl.Inspector.Contracts;

/// <summary>
/// Length-prefixed framing for <see cref="WireMessage"/>s over a duplex <see cref="Stream"/> (a named pipe
/// on Windows, a Unix domain socket elsewhere). Each frame is a 4-byte little-endian length followed by the
/// UTF-8 JSON body. A single connection has one reader and one writer driven sequentially (request then
/// response), so no internal locking is required.
/// </summary>
/// <remarks>
/// Inbound data is treated as untrusted: frame lengths are bounded and malformed JSON surfaces as an
/// exception the caller can handle rather than crashing the process.
/// </remarks>
public sealed class MessageChannel
{
    /// <summary>Reject frames larger than this (defensive bound against a hostile/​buggy peer).</summary>
    private const int MaxFrameBytes = 64 * 1024 * 1024;

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        // The wire stays small and human-debuggable; the polymorphic discriminator is emitted because we
        // always (de)serialize against the WireMessage base type.
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly Stream stream;

    public MessageChannel(Stream stream) => this.stream = stream;

    public async Task WriteAsync(WireMessage message, CancellationToken cancellationToken)
    {
        // Serialize against the base type so the $kind discriminator is written for the concrete subtype.
        var payload = JsonSerializer.SerializeToUtf8Bytes<WireMessage>(message, SerializerOptions);
        var header = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(header, payload.Length);

        await stream.WriteAsync(header, cancellationToken).ConfigureAwait(false);
        await stream.WriteAsync(payload, cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Reads the next message, or returns null when the peer has cleanly closed the connection
    /// (end of stream before a new frame begins).
    /// </summary>
    public async Task<WireMessage?> ReadAsync(CancellationToken cancellationToken)
    {
        var header = new byte[4];
        if (!await TryReadExactlyAsync(header, cancellationToken).ConfigureAwait(false))
            return null; // clean EOF — peer disconnected

        int length = BinaryPrimitives.ReadInt32LittleEndian(header);
        if (length < 0 || length > MaxFrameBytes)
            throw new InvalidDataException($"Inspector frame length {length} is out of range (0..{MaxFrameBytes}).");

        var payload = new byte[length];
        if (!await TryReadExactlyAsync(payload, cancellationToken).ConfigureAwait(false))
            throw new EndOfStreamException("Inspector connection ended mid-frame.");

        return JsonSerializer.Deserialize<WireMessage>(payload, SerializerOptions)
            ?? throw new InvalidDataException("Inspector frame deserialized to a null message.");
    }

    /// <summary>
    /// Fills <paramref name="buffer"/> completely. Returns false if the stream is at EOF before the first
    /// byte (clean close); throws if EOF is hit partway through a frame.
    /// </summary>
    private async Task<bool> TryReadExactlyAsync(byte[] buffer, CancellationToken cancellationToken)
    {
        int read = 0;
        while (read < buffer.Length)
        {
            int n = await stream.ReadAsync(buffer.AsMemory(read), cancellationToken).ConfigureAwait(false);
            if (n == 0)
            {
                if (read == 0) return false; // clean EOF at a frame boundary
                throw new EndOfStreamException("Inspector connection ended mid-frame.");
            }
            read += n;
        }
        return true;
    }
}
