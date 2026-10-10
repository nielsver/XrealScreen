using System.Collections.Concurrent;
using System.IO.Pipes;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using System.Threading.Channels;
using XrealScreen.Contracts;

namespace XrealScreen.Sdk.Client;

/// <summary>
/// Connection to the running XrealScreen app's local API (JSON-RPC 2.0 over the named pipe
/// <see cref="ApiProtocol.PipeName"/>). Thread-safe: calls may run concurrently.
/// </summary>
/// <remarks>
/// API errors throw <see cref="XrealScreenApiException"/> with a code from <see cref="ApiErrorCodes"/>.
/// When the app closes the connection, pending and later calls throw with
/// <see cref="ApiErrorCodes.Unavailable"/> and <see cref="Disconnected"/> is raised.
/// </remarks>
/// <example>
/// <code>
/// await using var app = await XrealScreenClient.ConnectAsync("My tool");
/// await app.StartHeadLookAsync();
/// await foreach (var pose in app.ReadPosesAsync(rateHz: 30, cancellationToken))
/// {
///     Console.WriteLine($"yaw {pose.Yaw:F1}  pitch {pose.Pitch:F1}");
/// }
/// </code>
/// </example>
public sealed class XrealScreenClient : IAsyncDisposable
{
    private static readonly TimeSpan DefaultConnectTimeout = TimeSpan.FromSeconds(3);

    private readonly JsonLineChannel _channel;
    private readonly ConcurrentDictionary<long, TaskCompletionSource<RpcMessage>> _pending = new();
    private readonly CancellationTokenSource _stop = new();
    private readonly Task _readLoop;
    private readonly Lock _poseLock = new();
    private Channel<PoseData>? _poseStream;
    private long _nextId;
    private volatile bool _connected = true;

    private XrealScreenClient(Stream stream)
    {
        _channel = new JsonLineChannel(stream);
        _readLoop = Task.Run(() => ReadLoopAsync(_stop.Token));
    }

    /// <summary>A pose notification arrived (after <see cref="SubscribePosesAsync"/> or during <see cref="ReadPosesAsync"/>). Raised on a background thread.</summary>
    public event EventHandler<PoseData>? PoseReceived;

    /// <summary>The app's state changed (after <see cref="SubscribeStatusAsync"/>). Raised on a background thread.</summary>
    public event EventHandler<StatusResult>? StatusChanged;

    /// <summary>The connection closed (app exited, API switched off, or <see cref="DisposeAsync"/>).</summary>
    public event EventHandler? Disconnected;

    /// <summary>API and app version, from the handshake.</summary>
    public HelloResult ServerInfo { get; private set; } = new();

    /// <summary>False after the connection closed.</summary>
    public bool IsConnected => _connected;

    /// <summary>
    /// Connects to the app and performs the <see cref="ApiMethods.Hello"/> handshake.
    /// </summary>
    /// <param name="clientName">Your program's name; shown in the app log.</param>
    /// <param name="timeout">How long to wait for the app; default 3 seconds.</param>
    /// <param name="pipeName">Pipe name; only change it for tests.</param>
    /// <param name="cancellationToken">Cancels the connection attempt.</param>
    /// <exception cref="XrealScreenApiException">
    /// <see cref="ApiErrorCodes.Unavailable"/>: XrealScreen is not running, or its local API is off (Settings page).
    /// </exception>
    public static async Task<XrealScreenClient> ConnectAsync(string? clientName = null, TimeSpan? timeout = null, string pipeName = ApiProtocol.PipeName, CancellationToken cancellationToken = default)
    {
        var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        try
        {
            await pipe.ConnectAsync(timeout ?? DefaultConnectTimeout, cancellationToken).ConfigureAwait(false);
        }
        catch (TimeoutException ex)
        {
            await pipe.DisposeAsync().ConfigureAwait(false);
            throw new XrealScreenApiException(ApiErrorCodes.Unavailable, $"XrealScreen is not running or its local API is off (no pipe \\\\.\\pipe\\{pipeName}). {ex.Message}");
        }
        catch
        {
            await pipe.DisposeAsync().ConfigureAwait(false);
            throw;
        }

        var client = new XrealScreenClient(pipe);
        try
        {
            client.ServerInfo = await client.CallAsync(ApiMethods.Hello, new HelloParams { ClientName = clientName }, ApiJsonContext.Default.HelloParams, ApiJsonContext.Default.HelloResult, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            await client.DisposeAsync().ConfigureAwait(false);
            throw;
        }

        return client;
    }

    // ---- status ---------------------------------------------------------------------------------

    /// <summary>Workspace, head look and glasses state.</summary>
    public Task<StatusResult> GetStatusAsync(CancellationToken cancellationToken = default) =>
        CallAsync(ApiMethods.StatusGet, ApiJsonContext.Default.StatusResult, cancellationToken);

    /// <summary>Raise <see cref="StatusChanged"/> now and whenever the state changes.</summary>
    public Task SubscribeStatusAsync(CancellationToken cancellationToken = default) => CallOkAsync(ApiMethods.StatusSubscribe, cancellationToken);

    /// <summary>Stop <see cref="StatusChanged"/>.</summary>
    public Task UnsubscribeStatusAsync(CancellationToken cancellationToken = default) => CallOkAsync(ApiMethods.StatusUnsubscribe, cancellationToken);

    // ---- poses ----------------------------------------------------------------------------------

    /// <summary>
    /// Raise <see cref="PoseReceived"/> up to <paramref name="rateHz"/> times per second while the app
    /// tracks the head (a workspace or head look runs; see <see cref="StatusResult.PoseSource"/>).
    /// </summary>
    public Task SubscribePosesAsync(int rateHz = 60, CancellationToken cancellationToken = default) =>
        CallAsync(ApiMethods.PoseSubscribe, new PoseSubscribeParams { RateHz = rateHz }, ApiJsonContext.Default.PoseSubscribeParams, ApiJsonContext.Default.OkResult, cancellationToken);

    /// <summary>Stop pose notifications.</summary>
    public Task UnsubscribePosesAsync(CancellationToken cancellationToken = default) => CallOkAsync(ApiMethods.PoseUnsubscribe, cancellationToken);

    /// <summary>
    /// Subscribes and streams poses until cancelled or disconnected, then unsubscribes. Poses arrive only
    /// while the app tracks the head; if the consumer is slow, the oldest poses are dropped.
    /// </summary>
    /// <exception cref="InvalidOperationException">Another <see cref="ReadPosesAsync"/> is running.</exception>
    public async IAsyncEnumerable<PoseData> ReadPosesAsync(int rateHz = 60, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var stream = Channel.CreateBounded<PoseData>(new BoundedChannelOptions(64) { FullMode = BoundedChannelFullMode.DropOldest, SingleReader = true });
        lock (_poseLock)
        {
            if (_poseStream is not null)
            {
                throw new InvalidOperationException("Poses are already being read.");
            }

            _poseStream = stream;
        }

        try
        {
            await SubscribePosesAsync(rateHz, cancellationToken).ConfigureAwait(false);
            while (true)
            {
                bool more;
                try
                {
                    more = await stream.Reader.WaitToReadAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    more = false;
                }

                if (!more)
                {
                    break;
                }

                while (stream.Reader.TryRead(out var pose))
                {
                    yield return pose;
                }
            }
        }
        finally
        {
            lock (_poseLock)
            {
                _poseStream = null;
            }

            if (_connected)
            {
                try
                {
                    await UnsubscribePosesAsync(CancellationToken.None).ConfigureAwait(false);
                }
                catch (XrealScreenApiException)
                {
                }
            }
        }
    }

    // ---- workspace ------------------------------------------------------------------------------

    /// <summary>Names of the saved workspaces.</summary>
    public async Task<IReadOnlyList<string>> ListWorkspacesAsync(CancellationToken cancellationToken = default) =>
        (await CallAsync(ApiMethods.WorkspaceList, ApiJsonContext.Default.WorkspaceListResult, cancellationToken).ConfigureAwait(false)).Names;

    /// <summary>
    /// Starts the latest workspace settings, or loads the saved workspace <paramref name="name"/> first.
    /// Returns when the workspace runs (a few seconds: virtual monitors are created).
    /// </summary>
    /// <exception cref="XrealScreenApiException">
    /// <see cref="ApiErrorCodes.Conflict"/> when a workspace already runs; <see cref="ApiErrorCodes.Unavailable"/>
    /// when <paramref name="name"/> does not exist; <see cref="ApiErrorCodes.Failed"/> when the start failed.
    /// </exception>
    public Task StartWorkspaceAsync(string? name = null, CancellationToken cancellationToken = default) =>
        CallAsync(ApiMethods.WorkspaceStart, new WorkspaceStartParams { Name = name }, ApiJsonContext.Default.WorkspaceStartParams, ApiJsonContext.Default.OkResult, cancellationToken);

    /// <summary>Stops the workspace; returns when the monitors are restored.</summary>
    public Task StopWorkspaceAsync(CancellationToken cancellationToken = default) => CallOkAsync(ApiMethods.WorkspaceStop, cancellationToken);

    /// <summary>Recenters the running workspace.</summary>
    public Task RecenterWorkspaceAsync(CancellationToken cancellationToken = default) => CallOkAsync(ApiMethods.WorkspaceRecenter, cancellationToken);

    /// <summary>Saves what the glasses show to <c>Pictures\XrealScreen</c>; returns the PNG path.</summary>
    public async Task<string> TakeScreenshotAsync(CancellationToken cancellationToken = default) =>
        (await CallAsync(ApiMethods.GlassesScreenshot, ApiJsonContext.Default.ScreenshotResult, cancellationToken).ConfigureAwait(false)).Path;

    // ---- head look ------------------------------------------------------------------------------

    /// <summary>Starts head look (Games page). Mouse output stays off until <see cref="SetHeadLookEnabledAsync"/> or Ctrl+Alt+H.</summary>
    public Task StartHeadLookAsync(CancellationToken cancellationToken = default) => CallOkAsync(ApiMethods.HeadLookStart, cancellationToken);

    /// <summary>Stops head look and restores the monitor layout.</summary>
    public Task StopHeadLookAsync(CancellationToken cancellationToken = default) => CallOkAsync(ApiMethods.HeadLookStop, cancellationToken);

    /// <summary>Switches head look's mouse output on or off.</summary>
    public Task SetHeadLookEnabledAsync(bool enabled, CancellationToken cancellationToken = default) =>
        CallAsync(ApiMethods.HeadLookSetEnabled, new SetEnabledParams { Enabled = enabled }, ApiJsonContext.Default.SetEnabledParams, ApiJsonContext.Default.OkResult, cancellationToken);

    /// <summary>Recenters head look.</summary>
    public Task RecenterHeadLookAsync(CancellationToken cancellationToken = default) => CallOkAsync(ApiMethods.HeadLookRecenter, cancellationToken);

    /// <summary>All head look settings.</summary>
    public Task<HeadLookSettings> GetHeadLookSettingsAsync(CancellationToken cancellationToken = default) =>
        CallAsync(ApiMethods.HeadLookGetSettings, ApiJsonContext.Default.HeadLookSettings, cancellationToken);

    /// <summary>Changes the non-null members of <paramref name="changes"/>; returns all settings.</summary>
    /// <example><c>await app.SetHeadLookSettingsAsync(new HeadLookSettings { Mode = HeadLookModes.Joystick, StickSpeed = 2000 });</c></example>
    public Task<HeadLookSettings> SetHeadLookSettingsAsync(HeadLookSettings changes, CancellationToken cancellationToken = default) =>
        CallAsync(ApiMethods.HeadLookSetSettings, changes, ApiJsonContext.Default.HeadLookSettings, ApiJsonContext.Default.HeadLookSettings, cancellationToken);

    // ---- raw calls ------------------------------------------------------------------------------

    /// <summary>Calls any method with raw JSON params; returns the raw result. For methods newer than this client.</summary>
    public async Task<JsonElement> CallRawAsync(string method, JsonElement? parameters = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(method);
        if (!_connected)
        {
            throw Closed();
        }

        long id = Interlocked.Increment(ref _nextId);
        var tcs = new TaskCompletionSource<RpcMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[id] = tcs;
        try
        {
            await _channel.WriteAsync(RpcMessage.Request(id, method, parameters), cancellationToken).ConfigureAwait(false);
            var response = await tcs.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
            if (response.Error is { } error)
            {
                throw new XrealScreenApiException(error.Code, error.Message);
            }

            return response.Result ?? default;
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException)
        {
            throw Closed();
        }
        finally
        {
            _pending.TryRemove(id, out _);
        }
    }

    /// <summary>Closes the connection. Subscriptions end with it.</summary>
    public async ValueTask DisposeAsync()
    {
        if (_stop.IsCancellationRequested)
        {
            return;
        }

        await _stop.CancelAsync().ConfigureAwait(false);
        await _channel.DisposeAsync().ConfigureAwait(false);
        try
        {
            await _readLoop.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }

        _stop.Dispose();
    }

    private async Task<TResult> CallAsync<TParams, TResult>(string method, TParams parameters, JsonTypeInfo<TParams> paramsType, JsonTypeInfo<TResult> resultType, CancellationToken cancellationToken)
    {
        var result = await CallRawAsync(method, JsonSerializer.SerializeToElement(parameters, paramsType), cancellationToken).ConfigureAwait(false);
        return result.Deserialize(resultType) ?? throw new XrealScreenApiException(ApiErrorCodes.InternalError, $"{method}: empty result.");
    }

    private async Task<TResult> CallAsync<TResult>(string method, JsonTypeInfo<TResult> resultType, CancellationToken cancellationToken)
    {
        var result = await CallRawAsync(method, null, cancellationToken).ConfigureAwait(false);
        return result.Deserialize(resultType) ?? throw new XrealScreenApiException(ApiErrorCodes.InternalError, $"{method}: empty result.");
    }

    private Task<JsonElement> CallOkAsync(string method, CancellationToken cancellationToken) => CallRawAsync(method, null, cancellationToken);

    private async Task ReadLoopAsync(CancellationToken stop)
    {
        try
        {
            while (!stop.IsCancellationRequested)
            {
                var read = await _channel.ReadAsync(stop).ConfigureAwait(false);
                if (read.Outcome == ReadOutcome.EndOfStream)
                {
                    break;
                }

                if (read.Outcome == ReadOutcome.Message)
                {
                    Dispatch(read.Message!);
                }
            }
        }
        catch (Exception ex) when (ex is IOException or OperationCanceledException or ObjectDisposedException)
        {
        }
        finally
        {
            _connected = false;
            foreach (var pending in _pending.Values)
            {
                pending.TrySetException(Closed());
            }

            lock (_poseLock)
            {
                _poseStream?.Writer.TryComplete();
            }

            Disconnected?.Invoke(this, EventArgs.Empty);
        }
    }

    private void Dispatch(RpcMessage message)
    {
        if (message.IsResponse)
        {
            if (message.Id is { ValueKind: JsonValueKind.Number } id && id.TryGetInt64(out long key) && _pending.TryGetValue(key, out var tcs))
            {
                tcs.TrySetResult(message);
            }

            return;
        }

        if (message.Params is not { } data)
        {
            return;
        }

        switch (message.Method)
        {
            case ApiNotifications.Pose when data.Deserialize(ApiJsonContext.Default.PoseData) is { } pose:
                lock (_poseLock)
                {
                    _poseStream?.Writer.TryWrite(pose);
                }

                PoseReceived?.Invoke(this, pose);
                break;
            case ApiNotifications.Status when data.Deserialize(ApiJsonContext.Default.StatusResult) is { } status:
                StatusChanged?.Invoke(this, status);
                break;
        }
    }

    private static XrealScreenApiException Closed() =>
        new(ApiErrorCodes.Unavailable, "The connection to XrealScreen is closed.");
}
