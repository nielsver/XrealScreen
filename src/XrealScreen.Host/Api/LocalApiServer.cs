using System.IO.Pipes;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using XrealScreen.Contracts;

namespace XrealScreen.Host.Api;

/// <summary>
/// The app's local API (ADR-0012): JSON-RPC 2.0 over the named pipe <see cref="ApiProtocol.PipeName"/>,
/// one JSON message per line. The pipe is created with <see cref="PipeOptions.CurrentUserOnly"/>, so
/// only programs of the same Windows user can connect; web pages cannot reach a named pipe.
/// Requests of one client run concurrently; pose and status notifications are sent to subscribers.
/// </summary>
public sealed class LocalApiServer : IAsyncDisposable
{
    private static readonly JsonElement NullId = JsonSerializer.SerializeToElement<JsonElement?>(null);
    private static readonly TimeSpan StatusPollInterval = TimeSpan.FromMilliseconds(250);

    private readonly ILocalApiHandler _handler;
    private readonly string _pipeName;
    private readonly int _maxClients;
    private readonly CancellationTokenSource _stop = new();
    private readonly Lock _clientsLock = new();
    private readonly HashSet<Task> _clients = [];
    private Task? _acceptLoop;
    private int _clientCount;
    private int _disposed;

    /// <param name="handler">The app side.</param>
    /// <param name="pipeName">Pipe name; tests use a unique one.</param>
    /// <param name="maxClients">Simultaneous connections.</param>
    public LocalApiServer(ILocalApiHandler handler, string pipeName = ApiProtocol.PipeName, int maxClients = 8)
    {
        _handler = handler ?? throw new ArgumentNullException(nameof(handler));
        _pipeName = pipeName;
        _maxClients = maxClients;
    }

    /// <summary>Connections, hellos and failures, for the app log.</summary>
    public event Action<string>? Log;

    /// <summary>Connected clients.</summary>
    public int ClientCount => Volatile.Read(ref _clientCount);

    /// <summary>Starts accepting clients.</summary>
    public void Start() => _acceptLoop ??= Task.Run(() => AcceptLoopAsync(_stop.Token));

    /// <summary>Disconnects all clients and stops listening. Safe to call more than once.</summary>
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1)
        {
            return;
        }

        await _stop.CancelAsync().ConfigureAwait(false);
        Task[] running;
        lock (_clientsLock)
        {
            running = [.. _clients, _acceptLoop ?? Task.CompletedTask];
        }

        try
        {
            await Task.WhenAll(running).WaitAsync(TimeSpan.FromSeconds(3)).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            Log?.Invoke("local API: clients did not disconnect in time");
        }

        _stop.Dispose();
    }

    private async Task AcceptLoopAsync(CancellationToken stop)
    {
        while (!stop.IsCancellationRequested)
        {
            NamedPipeServerStream pipe;
            try
            {
                pipe = new NamedPipeServerStream(_pipeName, PipeDirection.InOut, _maxClients, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            }
            catch (IOException ex)
            {
                // All instances busy (maxClients reached) or the name is taken by another process.
                Log?.Invoke($"local API: cannot listen on \\\\.\\pipe\\{_pipeName}: {ex.Message}");
                if (!await DelayAsync(TimeSpan.FromSeconds(1), stop).ConfigureAwait(false))
                {
                    return;
                }

                continue;
            }

            try
            {
                await pipe.WaitForConnectionAsync(stop).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is OperationCanceledException or IOException)
            {
                await pipe.DisposeAsync().ConfigureAwait(false);
                continue;
            }

            Task client = Task.Run(() => ServeClientAsync(pipe, stop), CancellationToken.None);
            lock (_clientsLock)
            {
                _clients.Add(client);
            }

            _ = client.ContinueWith(t => { lock (_clientsLock) { _clients.Remove(t); } }, TaskScheduler.Default);
        }
    }

    private async Task ServeClientAsync(NamedPipeServerStream pipe, CancellationToken stop)
    {
        Interlocked.Increment(ref _clientCount);
        using var clientStop = CancellationTokenSource.CreateLinkedTokenSource(stop);
        var session = new ClientSession(new JsonLineChannel(pipe), clientStop.Token);
        try
        {
            while (!clientStop.IsCancellationRequested)
            {
                var read = await session.Channel.ReadAsync(clientStop.Token).ConfigureAwait(false);
                if (read.Outcome == ReadOutcome.EndOfStream)
                {
                    break;
                }

                if (read.Outcome == ReadOutcome.Invalid)
                {
                    await session.SendAsync(RpcMessage.Failure(NullId, ApiErrorCodes.ParseError, read.Error ?? "Invalid message.")).ConfigureAwait(false);
                    continue;
                }

                var message = read.Message!;
                if (!message.IsCall)
                {
                    continue; // responses from a client are not expected; ignore
                }

                if (message.JsonRpc != ApiProtocol.JsonRpcVersion)
                {
                    await session.SendAsync(RpcMessage.Failure(message.Id ?? NullId, ApiErrorCodes.InvalidRequest, "\"jsonrpc\" must be \"2.0\".")).ConfigureAwait(false);
                    continue;
                }

                if (message.Id is null)
                {
                    continue; // a notification from the client: JSON-RPC says no response; nothing to do
                }

                _ = Task.Run(() => RespondAsync(session, message), CancellationToken.None);
            }
        }
        catch (Exception ex) when (ex is IOException or OperationCanceledException or ObjectDisposedException)
        {
        }
        finally
        {
            await clientStop.CancelAsync().ConfigureAwait(false);
            await session.Channel.DisposeAsync().ConfigureAwait(false);
            Interlocked.Decrement(ref _clientCount);
        }
    }

    private async Task RespondAsync(ClientSession session, RpcMessage request)
    {
        RpcMessage response;
        try
        {
            var result = await DispatchAsync(session, request.Method!, request.Params, session.Stop).ConfigureAwait(false);
            response = RpcMessage.Success(request.Id, result);
        }
        catch (XrealScreenApiException ex)
        {
            response = RpcMessage.Failure(request.Id, ex.Code, ex.Message);
        }
        catch (JsonException ex)
        {
            response = RpcMessage.Failure(request.Id, ApiErrorCodes.InvalidParams, $"Invalid params: {ex.Message}");
        }
        catch (OperationCanceledException) when (session.Stop.IsCancellationRequested)
        {
            return;
        }
        catch (Exception ex)
        {
            // Any other failure of the app: report it, never take the server down. No stack traces leave the process.
            Log?.Invoke($"local API: {request.Method} failed: {ex}");
            response = RpcMessage.Failure(request.Id, ApiErrorCodes.InternalError, ex.Message);
        }

        await session.SendAsync(response).ConfigureAwait(false);
    }

    private async Task<JsonElement> DispatchAsync(ClientSession session, string method, JsonElement? parameters, CancellationToken ct)
    {
        var h = _handler;
        switch (method)
        {
            case ApiMethods.Hello:
                var hello = Params(parameters, ApiJsonContext.Default.HelloParams) ?? new HelloParams();
                Log?.Invoke($"local API: client connected{(hello.ClientName is { Length: > 0 } n ? $" ({n})" : string.Empty)}");
                return Result(new HelloResult { ApiVersion = ApiProtocol.Version, AppVersion = h.AppVersion, Methods = ApiMethods.All }, ApiJsonContext.Default.HelloResult);
            case ApiMethods.StatusGet:
                return Result(h.GetStatus(), ApiJsonContext.Default.StatusResult);
            case ApiMethods.StatusSubscribe:
                session.SubscribeStatus(token => StatusLoopAsync(session, token));
                return Ok();
            case ApiMethods.StatusUnsubscribe:
                session.UnsubscribeStatus();
                return Ok();
            case ApiMethods.PoseSubscribe:
                var pose = Params(parameters, ApiJsonContext.Default.PoseSubscribeParams) ?? new PoseSubscribeParams();
                if (pose.RateHz is < 1 or > ApiProtocol.MaxPoseRateHz)
                {
                    throw new XrealScreenApiException(ApiErrorCodes.InvalidParams, $"rateHz must be 1–{ApiProtocol.MaxPoseRateHz}.");
                }

                session.SubscribePoses(token => PoseLoopAsync(session, pose.RateHz, token));
                return Ok();
            case ApiMethods.PoseUnsubscribe:
                session.UnsubscribePoses();
                return Ok();
            case ApiMethods.WorkspaceList:
                return Result(new WorkspaceListResult { Names = await h.ListWorkspacesAsync(ct).ConfigureAwait(false) }, ApiJsonContext.Default.WorkspaceListResult);
            case ApiMethods.WorkspaceStart:
                await h.StartWorkspaceAsync((Params(parameters, ApiJsonContext.Default.WorkspaceStartParams) ?? new WorkspaceStartParams()).Name, ct).ConfigureAwait(false);
                return Ok();
            case ApiMethods.WorkspaceStop:
                await h.StopWorkspaceAsync(ct).ConfigureAwait(false);
                return Ok();
            case ApiMethods.WorkspaceRecenter:
                await h.RecenterWorkspaceAsync(ct).ConfigureAwait(false);
                return Ok();
            case ApiMethods.GlassesScreenshot:
                return Result(new ScreenshotResult { Path = await h.TakeScreenshotAsync(ct).ConfigureAwait(false) }, ApiJsonContext.Default.ScreenshotResult);
            case ApiMethods.HeadLookStart:
                await h.StartHeadLookAsync(ct).ConfigureAwait(false);
                return Ok();
            case ApiMethods.HeadLookStop:
                await h.StopHeadLookAsync(ct).ConfigureAwait(false);
                return Ok();
            case ApiMethods.HeadLookSetEnabled:
                var enabled = Params(parameters, ApiJsonContext.Default.SetEnabledParams)
                    ?? throw new XrealScreenApiException(ApiErrorCodes.InvalidParams, "params {\"enabled\": true|false} required.");
                await h.SetHeadLookEnabledAsync(enabled.Enabled, ct).ConfigureAwait(false);
                return Ok();
            case ApiMethods.HeadLookRecenter:
                await h.RecenterHeadLookAsync(ct).ConfigureAwait(false);
                return Ok();
            case ApiMethods.HeadLookGetSettings:
                return Result(await h.GetHeadLookSettingsAsync(ct).ConfigureAwait(false), ApiJsonContext.Default.HeadLookSettings);
            case ApiMethods.HeadLookSetSettings:
                var changes = Params(parameters, ApiJsonContext.Default.HeadLookSettings)
                    ?? throw new XrealScreenApiException(ApiErrorCodes.InvalidParams, "params with at least one setting required.");
                if (changes.Mode is not (null or HeadLookModes.Look or HeadLookModes.Joystick))
                {
                    throw new XrealScreenApiException(ApiErrorCodes.InvalidParams, $"mode must be \"{HeadLookModes.Look}\" or \"{HeadLookModes.Joystick}\".");
                }

                return Result(await h.SetHeadLookSettingsAsync(changes, ct).ConfigureAwait(false), ApiJsonContext.Default.HeadLookSettings);
            default:
                throw new XrealScreenApiException(ApiErrorCodes.MethodNotFound, $"Unknown method \"{method}\".");
        }
    }

    private async Task PoseLoopAsync(ClientSession session, int rateHz, CancellationToken ct)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1.0 / rateHz));
        long lastTimestamp = long.MinValue;
        while (await timer.WaitForNextTickAsync(ct).ConfigureAwait(false))
        {
            if (_handler.GetLatestPose() is { } pose && pose.TimestampNs != lastTimestamp)
            {
                lastTimestamp = pose.TimestampNs;
                await session.SendAsync(RpcMessage.Notification(ApiNotifications.Pose, JsonSerializer.SerializeToElement(pose, ApiJsonContext.Default.PoseData))).ConfigureAwait(false);
            }
        }
    }

    private async Task StatusLoopAsync(ClientSession session, CancellationToken ct)
    {
        StatusResult? last = null;
        do
        {
            var status = _handler.GetStatus();
            if (status != last)
            {
                last = status;
                await session.SendAsync(RpcMessage.Notification(ApiNotifications.Status, JsonSerializer.SerializeToElement(status, ApiJsonContext.Default.StatusResult))).ConfigureAwait(false);
            }
        }
        while (await DelayAsync(StatusPollInterval, ct).ConfigureAwait(false));
    }

    private static T? Params<T>(JsonElement? parameters, JsonTypeInfo<T> type) where T : class =>
        parameters is { ValueKind: JsonValueKind.Object } p ? p.Deserialize(type)
        : parameters is null or { ValueKind: JsonValueKind.Null } ? null
        : throw new XrealScreenApiException(ApiErrorCodes.InvalidParams, "params must be an object.");

    private static JsonElement Result<T>(T value, JsonTypeInfo<T> type) => JsonSerializer.SerializeToElement(value, type);

    private static JsonElement Ok() => Result(new OkResult(), ApiJsonContext.Default.OkResult);

    private static async Task<bool> DelayAsync(TimeSpan delay, CancellationToken ct)
    {
        try
        {
            await Task.Delay(delay, ct).ConfigureAwait(false);
            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }

    /// <summary>One connection: its channel and notification subscriptions.</summary>
    private sealed class ClientSession(JsonLineChannel channel, CancellationToken stop)
    {
        private readonly Lock _lock = new();
        private CancellationTokenSource? _poses;
        private CancellationTokenSource? _status;

        public JsonLineChannel Channel { get; } = channel;

        public CancellationToken Stop { get; } = stop;

        /// <summary>Starts <paramref name="loop"/>, replacing an earlier pose subscription.</summary>
        public void SubscribePoses(Func<CancellationToken, Task> loop) => Restart(ref _poses, loop);

        public void UnsubscribePoses() => Cancel(ref _poses);

        /// <summary>Starts <paramref name="loop"/>, replacing an earlier status subscription.</summary>
        public void SubscribeStatus(Func<CancellationToken, Task> loop) => Restart(ref _status, loop);

        public void UnsubscribeStatus() => Cancel(ref _status);

        /// <summary>Sends a message; a closed connection ends the session (the read loop notices).</summary>
        public async Task SendAsync(RpcMessage message)
        {
            try
            {
                await Channel.WriteAsync(message, Stop).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is IOException or OperationCanceledException or ObjectDisposedException)
            {
            }
        }

        private void Restart(ref CancellationTokenSource? slot, Func<CancellationToken, Task> loop)
        {
            var cts = CancellationTokenSource.CreateLinkedTokenSource(Stop);
            CancellationTokenSource? old;
            lock (_lock)
            {
                old = slot;
                slot = cts;
            }

            old?.Cancel();
            old?.Dispose();
            var token = cts.Token;
            _ = Task.Run(async () =>
            {
                try
                {
                    await loop(token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                }
            }, CancellationToken.None);
        }

        private void Cancel(ref CancellationTokenSource? slot)
        {
            CancellationTokenSource? old;
            lock (_lock)
            {
                old = slot;
                slot = null;
            }

            old?.Cancel();
            old?.Dispose();
        }
    }
}
