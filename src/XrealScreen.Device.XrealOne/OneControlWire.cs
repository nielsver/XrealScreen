// Control-port framing ported from Skarian/one-xr (MIT, Copyright (c) 2026 Neil Skaria),
// XrControlProtocol.kt. See THIRD-PARTY-NOTICES.md and docs/legal/SOURCES.md.

using System.Buffers.Binary;
using System.Text;

namespace XrealScreen.Device.XrealOne;

/// <summary>
/// Request bytes for the glasses' control port (TCP 52999).
/// Magic values are the ones one-xr checks against its reference capture.
/// </summary>
internal static class OneControlWire
{
    public const int SetSceneMode = 0x2829;
    public const int SetDisplayInputMode = 0x2822;
    public const int SetBrightness = 0x271C;
    public const int SetDimmer = 0x2727;
    public const int GetSoftwareVersion = 0x271D;
    public const int GetId = 0x2729;
    public const int KeyStateChange = 0x272E;

    /// <summary>Scene value: glasses buttons stay usable.</summary>
    public const int SceneButtonsEnabled = 0;

    /// <summary>One picture, not side-by-side stereo.</summary>
    public const int DisplayInputRegular = 0;

    public static byte[] EncodeGetPropertyRequest() => [0x18, 0x00];

    public static byte[] EncodeSetNumericPropertyRequest(int value)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(value);
        byte[] valueBytes = EncodeVarint(value);
        int nestedLength = 1 + valueBytes.Length;
        byte[] nestedLengthBytes = EncodeVarint(nestedLength);
        var output = new byte[1 + nestedLengthBytes.Length + nestedLength];
        int offset = 0;
        output[offset++] = 0x1A;
        nestedLengthBytes.CopyTo(output, offset);
        offset += nestedLengthBytes.Length;
        output[offset++] = 0x08;
        valueBytes.CopyTo(output, offset);
        return output;
    }

    /// <summary>Header (magic, big-endian length) + outbound transaction id + body.</summary>
    public static byte[] EncodeRequest(int magic, int transactionId, ReadOnlySpan<byte> body)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(transactionId);
        int length = body.Length + 4;
        var packet = new byte[6 + length];
        packet[0] = (byte)(magic >> 8);
        packet[1] = (byte)magic;
        BinaryPrimitives.WriteInt32BigEndian(packet.AsSpan(2), length);
        BinaryPrimitives.WriteInt32BigEndian(packet.AsSpan(6), transactionId | int.MinValue);
        body.CopyTo(packet.AsSpan(10));
        return packet;
    }

    public static string ParseStringPropertyResponse(ReadOnlySpan<byte> payload)
    {
        var reader = new Reader(payload);
        Expect(reader.ReadVarint32(), 0x22, "property response");
        int rootLength = reader.ReadVarint32();
        var nested = new Reader(reader.ReadBytes(rootLength));
        Expect(reader.Remaining, 0, "bytes after property response");
        Expect(nested.ReadVarint32(), 0x12, "string value");
        int length = nested.ReadVarint32();
        string text = Encoding.UTF8.GetString(nested.ReadBytes(length));
        Expect(nested.Remaining, 0, "bytes after string value");
        return text;
    }

    public static void ParseEmptyPropertyResponse(ReadOnlySpan<byte> payload)
    {
        var reader = new Reader(payload);
        Expect(reader.ReadVarint32(), 0x22, "property response");
        int rootLength = reader.ReadVarint32();
        if (rootLength == 0)
        {
            Expect(reader.Remaining, 0, "bytes after an empty property response");
            return;
        }

        var nested = new Reader(reader.ReadBytes(rootLength));
        Expect(reader.Remaining, 0, "bytes after property response");
        if (nested.Remaining == 0)
        {
            return;
        }

        int tag = nested.ReadVarint32();
        if (tag == 0x08 && nested.Remaining > 0)
        {
            int status = nested.ReadVarint32();
            if (nested.Remaining == 0)
            {
                if (status == 0)
                {
                    return;
                }

                throw new InvalidOperationException($"Glasses rejected the command (status {status}).");
            }
        }

        throw new InvalidOperationException("Glasses returned a property response this app does not understand.");
    }

    private static void Expect(int actual, int expected, string what)
    {
        if (actual != expected)
        {
            throw new InvalidOperationException($"Unexpected {what}: {actual}.");
        }
    }

    private static byte[] EncodeVarint(int value)
    {
        var output = new List<byte>(5);
        uint remaining = (uint)value;
        while (true)
        {
            if ((remaining & ~0x7Fu) == 0)
            {
                output.Add((byte)remaining);
                return output.ToArray();
            }

            output.Add((byte)((remaining & 0x7F) | 0x80));
            remaining >>= 7;
        }
    }

    private ref struct Reader(ReadOnlySpan<byte> source)
    {
        private ReadOnlySpan<byte> _source = source;

        public int Remaining => _source.Length;

        public int ReadVarint32()
        {
            int shift = 0;
            int result = 0;
            while (shift < 35)
            {
                if (_source.Length == 0)
                {
                    throw new InvalidOperationException("Control response ended inside a number.");
                }

                byte current = _source[0];
                _source = _source[1..];
                result |= (current & 0x7F) << shift;
                if ((current & 0x80) == 0)
                {
                    return result;
                }

                shift += 7;
            }

            throw new InvalidOperationException("Control response number is too long.");
        }

        public ReadOnlySpan<byte> ReadBytes(int length)
        {
            if (length < 0 || length > _source.Length)
            {
                throw new InvalidOperationException("Control response is shorter than it claims.");
            }

            var slice = _source[..length];
            _source = _source[length..];
            return slice;
        }
    }
}
