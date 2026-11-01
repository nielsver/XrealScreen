using System.Buffers;

namespace XrealScreen.Device.XrealOne;

/// <summary>
/// One 8-bit grayscale image from the XREAL Eye (camera port). Pixels are row-major with
/// <see cref="Stride"/> bytes per row; only the first <see cref="Width"/> bytes of each row are image.
/// The pixel buffer is pooled: dispose the frame when done, and copy what you keep.
/// </summary>
public sealed class EyeFrame : IDisposable
{
    private byte[]? _buffer;

    internal EyeFrame(ulong timestampNs, int width, int height, int stride, ReadOnlySpan<byte> pixels)
    {
        TimestampNs = timestampNs;
        Width = width;
        Height = height;
        Stride = stride;
        _buffer = ArrayPool<byte>.Shared.Rent(stride * height);
        pixels[..(stride * height)].CopyTo(_buffer);
    }

    /// <summary>Device timestamp, nanoseconds (same clock as the IMU reports).</summary>
    public ulong TimestampNs { get; }

    public int Width { get; }

    public int Height { get; }

    public int Stride { get; }

    public ReadOnlySpan<byte> Pixels => _buffer is { } b ? b.AsSpan(0, Stride * Height) : throw new ObjectDisposedException(nameof(EyeFrame));

    /// <summary>Copies the image without row padding (<see cref="Width"/> × <see cref="Height"/> bytes).</summary>
    public byte[] ToPackedArray()
    {
        var packed = new byte[Width * Height];
        var pixels = Pixels;
        for (int y = 0; y < Height; y++)
        {
            pixels.Slice(y * Stride, Width).CopyTo(packed.AsSpan(y * Width));
        }

        return packed;
    }

    public void Dispose()
    {
        if (_buffer is { } b)
        {
            _buffer = null;
            ArrayPool<byte>.Shared.Return(b);
        }
    }
}
