using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using System.Text;

namespace XrealScreen.Device.XrealOne;

/// <summary>
/// ".xrcam" = XREAL Eye frames for offline work (hand-tracking tests, fixtures). Layout: "XRCAM1\n" +
/// UTF-8 metadata line + '\n', then records [u64 LE device ns][i64 LE host ticks (UTC)][i32 LE width]
/// [i32 LE height][width × height bytes, no row padding].
/// Recordings show the user's surroundings: keep them out of the repository unless made for that purpose.
/// </summary>
public sealed class XrcamWriter : IAsyncDisposable
{
    public static readonly byte[] Magic = "XRCAM1\n"u8.ToArray();
    internal const int RecordHeaderBytes = 24;
    private readonly Stream _stream;

    private XrcamWriter(Stream stream) => _stream = stream;

    public long FramesWritten { get; private set; }

    public static async Task<XrcamWriter> CreateAsync(string path, string metadata, CancellationToken cancellationToken = default)
    {
        var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read, 1024 * 1024, useAsync: true);
        await stream.WriteAsync(Magic, cancellationToken).ConfigureAwait(false);
        await stream.WriteAsync(Encoding.UTF8.GetBytes(metadata.Replace('\n', ' ') + "\n"), cancellationToken).ConfigureAwait(false);
        return new XrcamWriter(stream);
    }

    public async ValueTask WriteAsync(EyeFrame frame, DateTime receivedUtc, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(frame);
        var header = new byte[RecordHeaderBytes];
        BinaryPrimitives.WriteUInt64LittleEndian(header, frame.TimestampNs);
        BinaryPrimitives.WriteInt64LittleEndian(header.AsSpan(8), receivedUtc.Ticks);
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(16), frame.Width);
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(20), frame.Height);
        await _stream.WriteAsync(header, cancellationToken).ConfigureAwait(false);
        await _stream.WriteAsync(frame.ToPackedArray(), cancellationToken).ConfigureAwait(false);
        FramesWritten++;
    }

    public ValueTask DisposeAsync() => _stream.DisposeAsync();
}

public static class XrcamReader
{
    /// <summary>A recorded frame; <see cref="Pixels"/> has no row padding.</summary>
    public sealed record Frame(ulong TimestampNs, DateTime ReceivedUtc, int Width, int Height, byte[] Pixels);

    public static async IAsyncEnumerable<Frame> ReadAsync(string path, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 1024, useAsync: true);
        var magic = new byte[XrcamWriter.Magic.Length];
        await stream.ReadExactlyAsync(magic, cancellationToken).ConfigureAwait(false);
        if (!magic.AsSpan().SequenceEqual(XrcamWriter.Magic))
        {
            throw new InvalidDataException($"{path} is not an xrcam file.");
        }

        int b;
        while ((b = stream.ReadByte()) is not -1 and not '\n')
        {
        }

        var header = new byte[XrcamWriter.RecordHeaderBytes];
        while (true)
        {
            int read = await stream.ReadAtLeastAsync(header, header.Length, throwOnEndOfStream: false, cancellationToken).ConfigureAwait(false);
            if (read < header.Length)
            {
                yield break;
            }

            int width = BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(16));
            int height = BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(20));
            if (width is <= 0 or > 8192 || height is <= 0 or > 8192)
            {
                throw new InvalidDataException($"{path}: invalid frame size {width}x{height}.");
            }

            var pixels = new byte[width * height];
            await stream.ReadExactlyAsync(pixels, cancellationToken).ConfigureAwait(false);
            yield return new Frame(
                BinaryPrimitives.ReadUInt64LittleEndian(header),
                new DateTime(BinaryPrimitives.ReadInt64LittleEndian(header.AsSpan(8)), DateTimeKind.Utc),
                width,
                height,
                pixels);
        }
    }

    public static string ReadMetadata(string path)
    {
        using var stream = File.OpenRead(path);
        stream.Seek(XrcamWriter.Magic.Length, SeekOrigin.Begin);
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadLine() ?? string.Empty;
    }
}
