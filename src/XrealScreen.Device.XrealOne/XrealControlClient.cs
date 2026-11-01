using System.Net.Sockets;

namespace XrealScreen.Device.XrealOne;

/// <summary>
/// Read-only client for the glasses' control port (52999): sends GET requests and waits for the reply
/// with the same command and transaction id; other messages (key events) are skipped.
/// </summary>
public sealed class XrealControlClient : IAsyncDisposable
{
    /// <summary>Largest reply accepted (the calibration JSON is ~220 KB).</summary>
    public const int MaxReplyBytes = 4 * 1024 * 1024;

    private readonly TcpClient _client;
    private readonly NetworkStream _stream;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private int _nextTransactionId;

    private XrealControlClient(TcpClient client)
    {
        _client = client;
        _stream = client.GetStream();
    }

    public static async Task<XrealControlClient> ConnectAsync(OneEndpoints? endpoints, TimeSpan timeout, CancellationToken cancellationToken)
    {
        endpoints ??= OneEndpoints.Default;
        var client = new TcpClient { NoDelay = true };
        try
        {
            using var connectCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            connectCts.CancelAfter(timeout);
            await client.ConnectAsync(endpoints.Host, endpoints.ControlPort, connectCts.Token).ConfigureAwait(false);
            return new XrealControlClient(client);
        }
        catch
        {
            client.Dispose();
            throw;
        }
    }

    public Task<string> GetSoftwareVersionAsync(TimeSpan timeout, CancellationToken cancellationToken) =>
        GetStringAsync(ControlCommand.GetSoftwareVersion, timeout, cancellationToken);

    public Task<string> GetDspVersionAsync(TimeSpan timeout, CancellationToken cancellationToken) =>
        GetStringAsync(ControlCommand.GetDspVersion, timeout, cancellationToken);

    public Task<string> GetSerialNumberAsync(TimeSpan timeout, CancellationToken cancellationToken) =>
        GetStringAsync(ControlCommand.GetId, timeout, cancellationToken);

    /// <summary>Factory calibration JSON (cameras, IMU, display).</summary>
    public Task<string> GetConfigJsonAsync(TimeSpan timeout, CancellationToken cancellationToken) =>
        GetStringAsync(ControlCommand.GetConfig, timeout, cancellationToken);

    /// <summary>Sends a GET request and returns the string reply.</summary>
    public async Task<string> GetStringAsync(ushort command, TimeSpan timeout, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(timeout);
            int transactionId = Interlocked.Increment(ref _nextTransactionId) & int.MaxValue;
            transactionId = transactionId == 0 ? 1 : transactionId;
            await _stream.WriteAsync(ControlWire.EncodeRequest(command, transactionId, ControlWire.GetPropertyBody), cts.Token).ConfigureAwait(false);

            while (true)
            {
                var message = await ControlWire.ReadAsync(_stream, MaxReplyBytes, cts.Token).ConfigureAwait(false)
                    ?? throw new IOException("The glasses closed the control connection.");
                if (message.Magic == command && message.TransactionId == transactionId)
                {
                    return ControlWire.ParseString(message.Body);
                }
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException($"No reply to control command 0x{command:X4} within {timeout.TotalSeconds:F1} s.");
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _stream.DisposeAsync().ConfigureAwait(false);
        _client.Dispose();
        _gate.Dispose();
    }
}
