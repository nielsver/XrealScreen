using System.Buffers.Binary;
using XrealScreen.Device.XrealOne;

namespace XrealScreen.Device.Tests;

public class EyeFrameFramerTests
{
    /// <summary>Synthetic camera-port frame with the documented layout (pixel value = x + y).</summary>
    internal static byte[] BuildFrame(ulong timestampNs, int width = 6, int height = 4, int stride = 8, uint? bodyLength = null)
    {
        int body = EyeFrameFramer.BodyHeaderBytes + stride * height;
        var bytes = new byte[EyeFrameFramer.HeaderBytes + body];
        bytes[0] = 0x27;
        bytes[1] = 0x48;
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(2), bodyLength ?? (uint)body);
        var b = bytes.AsSpan(EyeFrameFramer.HeaderBytes);
        BinaryPrimitives.WriteUInt32LittleEndian(b[5..], (uint)width);
        BinaryPrimitives.WriteUInt32LittleEndian(b[9..], (uint)height);
        BinaryPrimitives.WriteUInt64LittleEndian(b[17..], timestampNs);
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < stride; x++)
            {
                b[EyeFrameFramer.BodyHeaderBytes + y * stride + x] = (byte)(x < width ? x + y : 0xEE);
            }
        }

        return bytes;
    }

    [Fact]
    public void DecodesHeaderAndPixels()
    {
        var framer = new EyeFrameFramer();
        var frames = new List<EyeFrame>();
        framer.Append(BuildFrame(16_680_000), frames.Add);

        using var f = Assert.Single(frames);
        Assert.Equal(16_680_000UL, f.TimestampNs);
        Assert.Equal((6, 4, 8), (f.Width, f.Height, f.Stride));
        var packed = f.ToPackedArray();
        Assert.Equal(24, packed.Length);
        Assert.Equal(3 + 2, packed[2 * 6 + 3]);
        Assert.DoesNotContain((byte)0xEE, packed);
    }

    [Fact]
    public void HandlesGarbageAndByteByByteDelivery()
    {
        var stream = new List<byte> { 0x00, 0x27, 0x99, 0x48 };
        stream.AddRange(BuildFrame(1));
        stream.AddRange(BuildFrame(2));

        var framer = new EyeFrameFramer();
        var frames = new List<EyeFrame>();
        foreach (byte b in stream)
        {
            framer.Append([b], frames.Add);
        }

        Assert.Equal([1UL, 2UL], frames.Select(f => f.TimestampNs));
        Assert.Equal(4, framer.Stats.DroppedBytes);
        frames.ForEach(f => f.Dispose());
    }

    [Fact]
    public void SplitAcrossChunks()
    {
        var bytes = BuildFrame(7);
        var framer = new EyeFrameFramer();
        var frames = new List<EyeFrame>();
        framer.Append(bytes.AsSpan(0, 100), frames.Add);
        Assert.Empty(frames);
        framer.Append(bytes.AsSpan(100), frames.Add);
        Assert.Single(frames).Dispose();
    }

    [Fact]
    public void SkipsInvalidLengthAndResyncs()
    {
        var stream = new List<byte>();
        stream.AddRange(BuildFrame(1, bodyLength: 10));
        stream.AddRange(BuildFrame(2, width: 9, stride: 8));
        stream.AddRange(BuildFrame(3));

        var framer = new EyeFrameFramer();
        var frames = new List<EyeFrame>();
        framer.Append(stream.ToArray(), frames.Add);

        Assert.Equal(3UL, Assert.Single(frames).TimestampNs);
        Assert.Equal(2, framer.Stats.InvalidHeaders);
        frames[0].Dispose();
    }

    [Fact]
    public void DisposedFrameThrows()
    {
        var framer = new EyeFrameFramer();
        EyeFrame? frame = null;
        framer.Append(BuildFrame(1), f => frame = f);
        frame!.Dispose();
        Assert.Throws<ObjectDisposedException>(() => frame.Pixels.Length);
    }
}
