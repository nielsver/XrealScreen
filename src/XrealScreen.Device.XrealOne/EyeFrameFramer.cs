using System.Buffers.Binary;

namespace XrealScreen.Device.XrealOne;

/// <summary>Running parse counters for the camera stream.</summary>
public sealed class EyeFramerStats
{
    public long BytesReceived { get; internal set; }
    public long DroppedBytes { get; internal set; }
    public long Frames { get; internal set; }
    public long InvalidHeaders { get; internal set; }

    public override string ToString() => $"bytes={BytesReceived} frames={Frames} dropped={DroppedBytes} badHeader={InvalidHeaders}";
}

/// <summary>
/// Splits the camera-port byte stream (52997) into frames. Own decoding, [verified-hw] XREAL 1S + Eye
/// 2026-10-29 (docs/findings/xreal-eye-camera.md):
/// header [0x27, 0x48, u32 BE body length], body = 320-byte header + image (stride × height, 8-bit).
/// Body header: u32 LE width @5, u32 LE height @9, u64 LE device timestamp (ns) @17.
/// </summary>
public sealed class EyeFrameFramer
{
    public const int HeaderBytes = 6;
    public const int BodyHeaderBytes = 320;
    private const byte Magic0 = 0x27;
    private const byte Magic1 = 0x48;
    private const int MaxBodyBytes = 4 * 1024 * 1024;

    private byte[] _buffer = new byte[512 * 1024];
    private int _count;

    public EyeFramerStats Stats { get; } = new();

    /// <summary>
    /// Appends received bytes and invokes <paramref name="onFrame"/> for each complete frame.
    /// The callback owns the frame and must dispose it.
    /// </summary>
    public void Append(ReadOnlySpan<byte> chunk, Action<EyeFrame> onFrame)
    {
        ArgumentNullException.ThrowIfNull(onFrame);
        Stats.BytesReceived += chunk.Length;
        EnsureCapacity(_count + chunk.Length);
        chunk.CopyTo(_buffer.AsSpan(_count));
        _count += chunk.Length;

        int offset = 0;
        while (_count - offset >= HeaderBytes)
        {
            var pending = _buffer.AsSpan(offset, _count - offset);
            int header = pending.IndexOf([Magic0, Magic1]);
            if (header < 0)
            {
                // Keep the last byte: it may be the first half of a split magic.
                Stats.DroppedBytes += pending.Length - 1;
                offset = _count - 1;
                break;
            }

            if (header > 0)
            {
                Stats.DroppedBytes += header;
                offset += header;
                continue;
            }

            uint bodyLength = BinaryPrimitives.ReadUInt32BigEndian(pending[2..]);
            if (bodyLength <= BodyHeaderBytes || bodyLength > MaxBodyBytes)
            {
                Stats.InvalidHeaders++;
                Stats.DroppedBytes++;
                offset++;
                continue;
            }

            if (pending.Length < HeaderBytes + bodyLength)
            {
                break;
            }

            var body = pending.Slice(HeaderBytes, (int)bodyLength);
            if (TryDecode(body, out var frame))
            {
                Stats.Frames++;
                onFrame(frame);
                offset += HeaderBytes + (int)bodyLength;
            }
            else
            {
                Stats.InvalidHeaders++;
                Stats.DroppedBytes++;
                offset++;
            }
        }

        Compact(offset);
    }

    internal static bool TryDecode(ReadOnlySpan<byte> body, out EyeFrame frame)
    {
        frame = null!;
        uint width = BinaryPrimitives.ReadUInt32LittleEndian(body[5..]);
        uint height = BinaryPrimitives.ReadUInt32LittleEndian(body[9..]);
        int imageBytes = body.Length - BodyHeaderBytes;
        if (width is 0 or > 8192 || height is 0 or > 8192 || imageBytes % height != 0)
        {
            return false;
        }

        int stride = imageBytes / (int)height;
        if (stride < width)
        {
            return false;
        }

        frame = new EyeFrame(BinaryPrimitives.ReadUInt64LittleEndian(body[17..]), (int)width, (int)height, stride, body[BodyHeaderBytes..]);
        return true;
    }

    private void Compact(int consumed)
    {
        int remaining = _count - consumed;
        if (consumed > 0 && remaining > 0)
        {
            Buffer.BlockCopy(_buffer, consumed, _buffer, 0, remaining);
        }

        _count = remaining;
    }

    private void EnsureCapacity(int size)
    {
        if (size > _buffer.Length)
        {
            Array.Resize(ref _buffer, Math.Max(size, _buffer.Length * 2));
        }
    }
}
