using System.Buffers;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Threading.Channels;

namespace XrealScreen.Device.XrealOne;

public sealed record XrealEyeCameraOptions
{
    public OneEndpoints Endpoints { get; init; } = OneEndpoints.Default;

    public TimeSpan ConnectTimeout { get; init; } = TimeSpan.FromSeconds(3);

    /// <summary>Frames buffered for a slow reader; older frames are dropped (and counted) beyond this.</summary>
    public int BufferedFrames { get; init; } = 8;
}

/// <summary>
/// Streams grayscale frames from the XREAL Eye over TCP (camera port 52997). The glasses send as soon
/// as a client connects; disconnecting stops it. Each frame must be disposed by the reader.
/// </summary>
public sealed class XrealEyeCameraSource
{
    private readonly XrealEyeCameraOptions _options;
    private long _droppedFrames;

    public XrealEyeCameraSource(XrealEyeCameraOptions? options = null) => _options = options ?? new XrealEyeCameraOptions();

    public EyeFrameFramer Framer { get; } = new();

    /// <summary>Frames dropped because the reader fell behind.</summary>
    public long DroppedFrames => Interlocked.Read(ref _droppedFrames);

    public async IAsyncEnumerable<EyeFrame> ReadAsync([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var channel = Channel.CreateBounded<EyeFrame>(
            new BoundedChannelOptions(_options.BufferedFrames)
            {
                SingleReader = true,
                SingleWriter = true,
                FullMode = BoundedChannelFullMode.DropOldest,
            },
            dropped =>
            {
                Interlocked.Increment(ref _droppedFrames);
                dropped.Dispose();
            });

        var pump = PumpAsync(channel.Writer, cancellationToken);
        try
        {
            await foreach (var frame in channel.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            {
                yield return frame;
            }
        }
        finally
        {
            while (channel.Reader.TryRead(out var left))
            {
                left.Dispose();
            }
        }

        await pump.ConfigureAwait(false);
    }

    private async Task PumpAsync(ChannelWriter<EyeFrame> writer, CancellationToken cancellationToken)
    {
        Exception? error = null;
        try
        {
            using var client = new TcpClient { NoDelay = true, ReceiveBufferSize = 1024 * 1024 };
            using (var connectCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            {
                connectCts.CancelAfter(_options.ConnectTimeout);
                await client.ConnectAsync(_options.Endpoints.Host, _options.Endpoints.CameraPort, connectCts.Token).ConfigureAwait(false);
            }

            await using var stream = client.GetStream();
            var buffer = ArrayPool<byte>.Shared.Rent(256 * 1024);
            try
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    int read = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
                    if (read == 0)
                    {
                        break;
                    }

                    Framer.Append(buffer.AsSpan(0, read), frame =>
                    {
                        if (!writer.TryWrite(frame))
                        {
                            frame.Dispose();
                        }
                    });
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex) when (ex is SocketException or IOException or OperationCanceledException)
        {
            error = new IOException($"Camera stream {_options.Endpoints.Host}:{_options.Endpoints.CameraPort} failed: {ex.Message}", ex);
        }
        finally
        {
            writer.TryComplete(error);
        }
    }
}
