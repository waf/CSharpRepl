// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Buffers.Binary;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace CSharpRepl.InjectedHook.Contracts;

/// <summary>
/// Length-prefixed framing for WireMessages over a duplex Stream (named pipe / Unix domain socket): each
/// frame is a 4-byte little-endian length followed by the UTF-8 JSON body.
///
/// - Inbound data is untrusted: frame lengths are bounded, and malformed JSON surfaces as an exception the
///   caller can handle rather than crashing the process.
/// - One logical reader per connection, but writes can race (an out-of-band cancel), so writes are
///   serialized by a mutex — a frame is never interleaved with another.
/// </summary>
public sealed class MessageChannel
{
    /// <summary>Reject frames larger than this (defensive bound against a hostile/​buggy peer).</summary>
    private const int MaxFrameBytes = 64 * 1024 * 1024;

    private readonly Stream stream;
    private readonly SemaphoreSlim writeGate = new(1, 1);

    public MessageChannel(Stream stream) => this.stream = stream;

    public async Task WriteAsync(WireMessage message, CancellationToken cancellationToken)
    {
        // Serialize against the base type so the $kind discriminator is written for the concrete subtype.
        var payload = JsonSerializer.SerializeToUtf8Bytes(message, WireJsonContext.Default.WireMessage);
        var header = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(header, payload.Length);

        // Serialize concurrent writes so a frame is never interleaved with another (e.g. a cancel message sent
        // while the next request is being written).
        await writeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await stream.WriteAsync(header, cancellationToken).ConfigureAwait(false);
            await stream.WriteAsync(payload, cancellationToken).ConfigureAwait(false);
            await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            writeGate.Release();
        }
    }

    /// <summary>Reads the next message, or null when the peer cleanly closed (EOF before a new frame begins).</summary>
    public async Task<WireMessage?> ReadAsync(CancellationToken cancellationToken)
    {
        var header = new byte[4];
        if (!await TryReadExactlyAsync(header, cancellationToken).ConfigureAwait(false))
        {
            return null; // clean EOF — peer disconnected
        }

        int length = BinaryPrimitives.ReadInt32LittleEndian(header);
        if (length < 0 || length > MaxFrameBytes)
        {
            throw new InvalidDataException($"Connector frame length {length} is out of range (0..{MaxFrameBytes}).");
        }

        var payload = new byte[length];
        if (!await TryReadExactlyAsync(payload, cancellationToken).ConfigureAwait(false))
        {
            throw new EndOfStreamException("Connector connection ended mid-frame.");
        }

        return JsonSerializer.Deserialize(payload, WireJsonContext.Default.WireMessage)
            ?? throw new InvalidDataException("Connector frame deserialized to a null message.");
    }

    /// <summary>Fills the buffer completely. False on EOF before the first byte (clean close); throws on EOF mid-frame.</summary>
    private async Task<bool> TryReadExactlyAsync(byte[] buffer, CancellationToken cancellationToken)
    {
        int read = 0;
        while (read < buffer.Length)
        {
            int n = await stream.ReadAsync(buffer.AsMemory(read), cancellationToken).ConfigureAwait(false);
            if (n == 0)
            {
                if (read == 0)
                {
                    return false; // clean EOF at a frame boundary
                }

                throw new EndOfStreamException("Connector connection ended mid-frame.");
            }
            read += n;
        }
        return true;
    }
}
