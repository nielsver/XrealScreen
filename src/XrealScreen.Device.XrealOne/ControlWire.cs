// Control-port framing and property encoding ported from Skarian/one-xr (MIT, Copyright (c) 2026 Neil Skaria),
// XrControlProtocol.kt. See THIRD-PARTY-NOTICES.md and docs/legal/SOURCES.md.

using System.Buffers.Binary;
using System.Text;

namespace XrealScreen.Device.XrealOne;

/// <summary>Command ids ("magic") on the control port. GET commands are read-only.</summary>
public static class ControlCommand
{
    /// <summary>Software version, e.g. "1.9.1.20260914175625". [verified-hw] 1S 2026-10-29.</summary>
    public const ushort GetSoftwareVersion = 0x271D;

    /// <summary>DSP version, e.g. "15.A.01.097_20250430". [verified-hw] 1S 2026-10-29.</summary>
    public const ushort GetDspVersion = 0x272D;

    /// <summary>Serial number. [verified-hw] 1S 2026-10-29.</summary>
    public const ushort GetId = 0x2729;

    /// <summary>Factory calibration JSON (~220 KB). [verified-hw] 1S 2026-10-29.</summary>
    public const ushort GetConfig = 0x271F;
}

/// <summary>A control-port message: [u16 BE magic][u32 BE length = 4 + body][u32 BE transaction id][body].</summary>
public readonly record struct ControlMessage(ushort Magic, int TransactionId, byte[] Body);

/// <summary>Encoding and decoding of control-port messages (protobuf-like property bodies).</summary>
public static class ControlWire
{
    public const int HeaderBytes = 6;
    private const byte RootFieldTag = 0x22;
    private const byte GetPropertyTag = 0x18;
    private const byte NumericValueTag = 0x08;
    private const byte StringValueTag = 0x12;

    /// <summary>Body of every GET request.</summary>
    public static ReadOnlySpan<byte> GetPropertyBody => [GetPropertyTag, 0x00];

    /// <summary>Encodes a request; outbound transaction ids carry the high bit.</summary>
    public static byte[] EncodeRequest(ushort magic, int transactionId, ReadOnlySpan<byte> body)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(transactionId);
        var bytes = new byte[HeaderBytes + 4 + body.Length];
        BinaryPrimitives.WriteUInt16BigEndian(bytes, magic);
        BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(2), 4 + body.Length);
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(6), (uint)transactionId | 0x8000_0000u);
        body.CopyTo(bytes.AsSpan(10));
        return bytes;
    }

    /// <summary>Encodes a reply the way the glasses send it (used by tests and fakes).</summary>
    public static byte[] EncodeReply(ushort magic, int transactionId, ReadOnlySpan<byte> body)
    {
        var bytes = new byte[HeaderBytes + 4 + body.Length];
        BinaryPrimitives.WriteUInt16BigEndian(bytes, magic);
        BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(2), 4 + body.Length);
        BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(6), transactionId);
        body.CopyTo(bytes.AsSpan(10));
        return bytes;
    }

    /// <summary>Reads one message; returns null at end of stream.</summary>
    public static async Task<ControlMessage?> ReadAsync(Stream stream, int maxBodyBytes, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(stream);
        var header = new byte[HeaderBytes];
        int read = await stream.ReadAtLeastAsync(header, header.Length, throwOnEndOfStream: false, cancellationToken).ConfigureAwait(false);
        if (read < header.Length)
        {
            return null;
        }

        ushort magic = BinaryPrimitives.ReadUInt16BigEndian(header);
        int length = BinaryPrimitives.ReadInt32BigEndian(header.AsSpan(2));
        if (length < 4 || length - 4 > maxBodyBytes)
        {
            throw new InvalidDataException($"Control message 0x{magic:X4} has invalid length {length}.");
        }

        var payload = new byte[length];
        await stream.ReadExactlyAsync(payload, cancellationToken).ConfigureAwait(false);
        int transactionId = BinaryPrimitives.ReadInt32BigEndian(payload) & int.MaxValue;
        return new ControlMessage(magic, transactionId, payload[4..]);
    }

    /// <summary>String reply body: 22 &lt;len&gt; [12 &lt;len&gt; utf8].</summary>
    public static string ParseString(ReadOnlySpan<byte> body)
    {
        var nested = ReadRoot(body);
        int offset = 0;
        int tag = ReadVarint(nested, ref offset);
        if (tag != StringValueTag)
        {
            throw new InvalidDataException(tag == NumericValueTag
                ? $"Glasses rejected the request (status {ReadVarint(nested, ref offset)})."
                : $"Unexpected string reply tag 0x{tag:X2}.");
        }

        int length = ReadVarint(nested, ref offset);
        if (offset + length != nested.Length)
        {
            throw new InvalidDataException("String reply length does not match the body.");
        }

        return Encoding.UTF8.GetString(nested.Slice(offset, length));
    }

    /// <summary>String reply body built the way the glasses send it (used by tests and fakes).</summary>
    public static byte[] EncodeStringReplyBody(string value)
    {
        byte[] text = Encoding.UTF8.GetBytes(value);
        var nested = new List<byte> { StringValueTag };
        nested.AddRange(EncodeVarint(text.Length));
        nested.AddRange(text);
        var body = new List<byte> { RootFieldTag };
        body.AddRange(EncodeVarint(nested.Count));
        body.AddRange(nested);
        return [.. body];
    }

    private static ReadOnlySpan<byte> ReadRoot(ReadOnlySpan<byte> body)
    {
        int offset = 0;
        int tag = ReadVarint(body, ref offset);
        if (tag != RootFieldTag)
        {
            throw new InvalidDataException($"Unexpected reply root tag 0x{tag:X2}.");
        }

        int length = ReadVarint(body, ref offset);
        if (offset + length != body.Length)
        {
            throw new InvalidDataException("Reply root length does not match the body.");
        }

        return body.Slice(offset, length);
    }

    internal static int ReadVarint(ReadOnlySpan<byte> data, ref int offset)
    {
        long value = 0;
        for (int shift = 0; shift < 35; shift += 7)
        {
            if (offset >= data.Length)
            {
                throw new InvalidDataException("Truncated varint.");
            }

            byte b = data[offset++];
            value |= (long)(b & 0x7F) << shift;
            if ((b & 0x80) == 0)
            {
                return value <= int.MaxValue ? (int)value : throw new InvalidDataException("Varint too large.");
            }
        }

        throw new InvalidDataException("Varint too long.");
    }

    internal static byte[] EncodeVarint(int value)
    {
        var bytes = new List<byte>(5);
        uint v = (uint)value;
        while (v >= 0x80)
        {
            bytes.Add((byte)(v | 0x80));
            v >>= 7;
        }

        bytes.Add((byte)v);
        return [.. bytes];
    }
}
