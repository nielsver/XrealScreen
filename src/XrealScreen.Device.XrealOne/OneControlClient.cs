using System.Buffers.Binary;
using System.Net.Sockets;

namespace XrealScreen.Device.XrealOne;

/// <summary>
/// One short conversation with the glasses' control port. The picture can be up while this port is closed.
/// </summary>
public sealed class OneControlClient : IAsyncDisposable
{
    private readonly TcpClient _client;
    private readonly NetworkStream _stream;
    private readonly TimeSpan _commandTimeout;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private int _nextTransaction = 1;

    private OneControlClient(TcpClient client, NetworkStream stream, TimeSpan commandTimeout)
    {
        _client = client;
        _stream = stream;
        _commandTimeout = commandTimeout;
    }

    public static async Task<OneControlClient> ConnectAsync(string host, int port, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var client = new TcpClient { NoDelay = true };
        try
        {
            using var connect = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            connect.CancelAfter(timeout);
            await client.ConnectAsync(host, port, connect.Token).ConfigureAwait(false);
            return new OneControlClient(client, client.GetStream(), timeout);
        }
        catch
        {
            client.Dispose();
            throw;
        }
    }

    public Task<string> GetIdAsync(CancellationToken cancellationToken) =>
        GetStringAsync(OneControlWire.GetId, cancellationToken);

    public Task<string> GetSoftwareVersionAsync(CancellationToken cancellationToken) =>
        GetStringAsync(OneControlWire.GetSoftwareVersion, cancellationToken);

    /// <summary>Asks the glasses to show one picture instead of side-by-side stereo.</summary>
    public Task SetRegularPictureAsync(CancellationToken cancellationToken) =>
        SetNumberAsync(OneControlWire.SetDisplayInputMode, OneControlWire.DisplayInputRegular, cancellationToken);

    /// <summary>Leaves the glasses buttons enabled. This does not switch Anchor and Follow.</summary>
    public Task SetButtonsEnabledAsync(CancellationToken cancellationToken) =>
        SetNumberAsync(OneControlWire.SetSceneMode, OneControlWire.SceneButtonsEnabled, cancellationToken);

    /// <summary>Display brightness, 0 through 9.</summary>
    public Task SetBrightnessAsync(int level, CancellationToken cancellationToken) =>
        SetNumberAsync(OneControlWire.SetBrightness, Math.Clamp(level, 0, 9), cancellationToken);

    /// <summary>Electrochromic dimmer: 0 clear, 1 middle, 2 dark.</summary>
    public Task SetDimmerAsync(int level, CancellationToken cancellationToken) =>
        SetNumberAsync(OneControlWire.SetDimmer, Math.Clamp(level, 0, 2), cancellationToken);

    public ValueTask DisposeAsync()
    {
        _stream.Dispose();
        _client.Dispose();
        _gate.Dispose();
        return ValueTask.CompletedTask;
    }

    private async Task<string> GetStringAsync(int magic, CancellationToken cancellationToken)
    {
        byte[] payload = await ExchangeAsync(magic, OneControlWire.EncodeGetPropertyRequest(), cancellationToken).ConfigureAwait(false);
        return OneControlWire.ParseStringPropertyResponse(payload);
    }

    private async Task SetNumberAsync(int magic, int value, CancellationToken cancellationToken)
    {
        byte[] payload = await ExchangeAsync(magic, OneControlWire.EncodeSetNumericPropertyRequest(value), cancellationToken).ConfigureAwait(false);
        OneControlWire.ParseEmptyPropertyResponse(payload);
    }

    private async Task<byte[]> ExchangeAsync(int magic, byte[] body, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            int transaction = _nextTransaction++;
            byte[] packet = OneControlWire.EncodeRequest(magic, transaction, body);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(_commandTimeout);
            await _stream.WriteAsync(packet, timeout.Token).ConfigureAwait(false);
            await _stream.FlushAsync(timeout.Token).ConfigureAwait(false);

            while (true)
            {
                byte[] header = await ReadExactAsync(6, timeout.Token).ConfigureAwait(false);
                int responseMagic = (header[0] << 8) | header[1];
                int length = BinaryPrimitives.ReadInt32BigEndian(header.AsSpan(2));
                if (length < 0 || length > 1_048_576)
                {
                    throw new InvalidOperationException("Control response length is not usable.");
                }

                byte[] response = await ReadExactAsync(length, timeout.Token).ConfigureAwait(false);
                if (responseMagic == OneControlWire.KeyStateChange || response.Length < 4)
                {
                    continue;
                }

                int id = BinaryPrimitives.ReadInt32BigEndian(response) & int.MaxValue;
                if (responseMagic == magic && id == transaction)
                {
                    return response[4..];
                }
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<byte[]> ReadExactAsync(int count, CancellationToken cancellationToken)
    {
        var buffer = new byte[count];
        int offset = 0;
        while (offset < count)
        {
            int read = await _stream.ReadAsync(buffer.AsMemory(offset, count - offset), cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                throw new IOException("The glasses closed the control connection.");
            }

            offset += read;
        }

        return buffer;
    }
}
