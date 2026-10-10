using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using XrealScreen.Contracts;
using XrealScreen.Host.Api;
using XrealScreen.Sdk.Client;

namespace XrealScreen.Api.Tests;

/// <summary>Server and client end to end over a real named pipe (unique name per test).</summary>
public sealed class LocalApiTests : IAsyncDisposable
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);
    private readonly string _pipe = $"XrealScreen.Api.Test.{Guid.NewGuid():N}";
    private readonly FakeHandler _handler = new();
    private readonly LocalApiServer _server;

    public LocalApiTests()
    {
        _server = new LocalApiServer(_handler, _pipe);
        _server.Start();
    }

    public async ValueTask DisposeAsync() => await _server.DisposeAsync();

    private Task<XrealScreenClient> ConnectAsync(string? name = "tests") =>
        XrealScreenClient.ConnectAsync(name, Timeout, _pipe, TestContext.Current.CancellationToken);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Connect_HandshakeReportsVersionsAndMethods()
    {
        await using var client = await ConnectAsync();

        Assert.Equal(ApiProtocol.Version, client.ServerInfo.ApiVersion);
        Assert.Equal("9.9.9-test", client.ServerInfo.AppVersion);
        Assert.Equal(ApiMethods.All, client.ServerInfo.Methods);
        Assert.True(client.IsConnected);
    }

    [Fact]
    public async Task Connect_NoServer_ThrowsUnavailable()
    {
        var ex = await Assert.ThrowsAsync<XrealScreenApiException>(() =>
            XrealScreenClient.ConnectAsync("tests", TimeSpan.FromMilliseconds(200), $"XrealScreen.Api.Missing.{Guid.NewGuid():N}", Ct));

        Assert.Equal(ApiErrorCodes.Unavailable, ex.Code);
    }

    [Fact]
    public async Task GetStatus_ReturnsHandlerState()
    {
        _handler.SetStatus(new StatusResult
        {
            Workspace = new WorkspaceStatus { State = WorkspaceStates.Running, Name = "Desk" },
            HeadLook = new HeadLookStatus { Running = false },
            Glasses = new GlassesStatus { DisplayConnected = true, DisplayName = "XREAL 1S", Model = "XReal1S" },
            PoseSource = PoseSources.Workspace,
        });
        await using var client = await ConnectAsync();

        var status = await client.GetStatusAsync(Ct);

        Assert.Equal(WorkspaceStates.Running, status.Workspace.State);
        Assert.Equal("Desk", status.Workspace.Name);
        Assert.Equal("XREAL 1S", status.Glasses.DisplayName);
        Assert.Equal(PoseSources.Workspace, status.PoseSource);
    }

    [Fact]
    public async Task WorkspaceAndHeadLookCommands_ReachTheHandler()
    {
        await using var client = await ConnectAsync();

        Assert.Equal(["Desk", "Games"], await client.ListWorkspacesAsync(Ct));
        await client.StartWorkspaceAsync("Games", Ct);
        await client.RecenterWorkspaceAsync(Ct);
        await client.StopWorkspaceAsync(Ct);
        Assert.Equal(@"C:\Pictures\XrealScreen\shot.png", await client.TakeScreenshotAsync(Ct));
        await client.StartHeadLookAsync(Ct);
        await client.SetHeadLookEnabledAsync(true, Ct);
        await client.RecenterHeadLookAsync(Ct);
        await client.StopHeadLookAsync(Ct);

        Assert.Equal("Games", _handler.StartedWorkspace);
        Assert.True(_handler.HeadLookEnabled);
        Assert.Equal(
            ["list", "start", "recenter", "stop", "screenshot", "headLook.start", "headLook.setEnabled", "headLook.recenter", "headLook.stop"],
            _handler.Calls);
    }

    [Fact]
    public async Task StartWorkspace_WithoutName_PassesNull()
    {
        await using var client = await ConnectAsync();

        await client.StartWorkspaceAsync(cancellationToken: Ct);

        Assert.Equal("(latest)", _handler.StartedWorkspace);
    }

    [Fact]
    public async Task HeadLookSettings_PartialUpdateKeepsOtherValues()
    {
        await using var client = await ConnectAsync();

        var updated = await client.SetHeadLookSettingsAsync(new HeadLookSettings { Mode = HeadLookModes.Joystick, StickSpeed = 2000 }, Ct);
        var read = await client.GetHeadLookSettingsAsync(Ct);

        Assert.Equal(HeadLookModes.Joystick, updated.Mode);
        Assert.Equal(2000, updated.StickSpeed);
        Assert.Equal(20, updated.Sensitivity);
        Assert.Equal(updated, read);
    }

    [Fact]
    public async Task HandlerApiError_ReachesClientWithCode()
    {
        await using var client = await ConnectAsync();

        var ex = await Assert.ThrowsAsync<XrealScreenApiException>(() => client.StartWorkspaceAsync("Missing", Ct));

        Assert.Equal(ApiErrorCodes.Unavailable, ex.Code);
        Assert.Contains("Missing", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task HandlerCrash_IsInternalError_AndServerKeepsWorking()
    {
        await using var client = await ConnectAsync();
        _handler.Throw = new InvalidOperationException("boom");

        var ex = await Assert.ThrowsAsync<XrealScreenApiException>(() => client.StopWorkspaceAsync(Ct));
        var status = await client.GetStatusAsync(Ct);

        Assert.Equal(ApiErrorCodes.InternalError, ex.Code);
        Assert.Equal("boom", ex.Message);
        Assert.NotNull(status);
    }

    [Theory]
    [InlineData("no.such.method", null, ApiErrorCodes.MethodNotFound)]
    [InlineData(ApiMethods.PoseSubscribe, """{"rateHz":0}""", ApiErrorCodes.InvalidParams)]
    [InlineData(ApiMethods.PoseSubscribe, """{"rateHz":1000}""", ApiErrorCodes.InvalidParams)]
    [InlineData(ApiMethods.HeadLookSetEnabled, null, ApiErrorCodes.InvalidParams)]
    [InlineData(ApiMethods.HeadLookSetEnabled, """{"enabled":"yes"}""", ApiErrorCodes.InvalidParams)]
    [InlineData(ApiMethods.HeadLookSetSettings, """{"mode":"spin"}""", ApiErrorCodes.InvalidParams)]
    [InlineData(ApiMethods.WorkspaceStart, "[1,2]", ApiErrorCodes.InvalidParams)]
    public async Task BadRequests_ReturnJsonRpcErrors(string method, string? parameters, int expectedCode)
    {
        await using var client = await ConnectAsync();
        JsonElement? p = parameters is null ? null : JsonDocument.Parse(parameters).RootElement.Clone();

        var ex = await Assert.ThrowsAsync<XrealScreenApiException>(() => client.CallRawAsync(method, p, Ct));

        Assert.Equal(expectedCode, ex.Code);
    }

    [Fact]
    public async Task SlowRequest_DoesNotBlockOtherRequestsOfTheSameClient()
    {
        _handler.StartGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var client = await ConnectAsync();

        var start = client.StartWorkspaceAsync("Desk", Ct);
        var status = await client.GetStatusAsync(Ct).WaitAsync(Timeout, Ct);
        Assert.False(start.IsCompleted);
        _handler.StartGate.SetResult();
        await start.WaitAsync(Timeout, Ct);

        Assert.NotNull(status);
        Assert.Equal("Desk", _handler.StartedWorkspace);
    }

    [Fact]
    public async Task ReadPoses_StreamsNewPosesOnly_WithSource()
    {
        await using var client = await ConnectAsync();
        _handler.SetPose(1, 1f);
        var received = new List<PoseData>();
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        cts.CancelAfter(Timeout);

        await foreach (var pose in client.ReadPosesAsync(rateHz: 200, cts.Token))
        {
            received.Add(pose);
            if (received.Count == 3)
            {
                break;
            }

            // Same timestamp for a while: must not be resent.
            await Task.Delay(50, cts.Token);
            _handler.SetPose(pose.TimestampNs + 1, pose.Yaw + 1);
        }

        Assert.Equal([1L, 2L, 3L], received.Select(p => p.TimestampNs));
        Assert.Equal([1f, 2f, 3f], received.Select(p => p.Yaw));
        Assert.All(received, p => Assert.Equal(PoseSources.HeadLook, p.Source));
    }

    [Fact]
    public async Task ReadPoses_SecondConcurrentReader_Throws()
    {
        await using var client = await ConnectAsync();
        _handler.SetPose(1, 0);
        await using var first = client.ReadPosesAsync(60, Ct).GetAsyncEnumerator(Ct);
        Assert.True(await first.MoveNextAsync().AsTask().WaitAsync(Timeout, Ct));

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await foreach (var _ in client.ReadPosesAsync(60, Ct))
            {
            }
        });
    }

    [Fact]
    public async Task StatusSubscription_SendsCurrentStateThenChanges()
    {
        await using var client = await ConnectAsync();
        var statuses = new System.Collections.Concurrent.BlockingCollection<StatusResult>();
        client.StatusChanged += (_, s) => statuses.Add(s);

        await client.SubscribeStatusAsync(Ct);
        Assert.True(statuses.TryTake(out var first, Timeout));
        _handler.SetStatus(new StatusResult { Workspace = new WorkspaceStatus { State = WorkspaceStates.Starting } });
        Assert.True(statuses.TryTake(out var second, Timeout));

        Assert.Equal(WorkspaceStates.Stopped, first!.Workspace.State);
        Assert.Equal(WorkspaceStates.Starting, second!.Workspace.State);
    }

    [Fact]
    public async Task SeveralClients_AreServedAtTheSameTime()
    {
        var clients = await Task.WhenAll(Enumerable.Range(0, 4).Select(i => ConnectAsync($"client {i}")));
        try
        {
            var statuses = await Task.WhenAll(clients.Select(c => c.GetStatusAsync(Ct)));

            Assert.Equal(4, statuses.Length);
            Assert.Equal(4, _server.ClientCount);
        }
        finally
        {
            foreach (var c in clients)
            {
                await c.DisposeAsync();
            }
        }
    }

    [Fact]
    public async Task ServerStops_ClientIsDisconnected_AndCallsThrowUnavailable()
    {
        var client = await ConnectAsync();
        var disconnected = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        client.Disconnected += (_, _) => disconnected.TrySetResult();

        await _server.DisposeAsync();
        await disconnected.Task.WaitAsync(Timeout, Ct);
        var ex = await Assert.ThrowsAsync<XrealScreenApiException>(() => client.GetStatusAsync(Ct));

        Assert.False(client.IsConnected);
        Assert.Equal(ApiErrorCodes.Unavailable, ex.Code);
        await client.DisposeAsync();
    }

    // ---- raw protocol (what a Python or Node client sends) --------------------------------------

    private async Task<(NamedPipeClientStream Pipe, StreamReader Reader)> OpenRawAsync()
    {
        var pipe = new NamedPipeClientStream(".", _pipe, PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        await pipe.ConnectAsync(Timeout, Ct);
        return (pipe, new StreamReader(pipe, Encoding.UTF8));
    }

    private static async Task SendLineAsync(Stream pipe, string line)
    {
        await pipe.WriteAsync(Encoding.UTF8.GetBytes(line + "\n"), Ct);
        await pipe.FlushAsync(Ct);
    }

    private static async Task<JsonElement> ReadLineAsync(StreamReader reader) =>
        JsonDocument.Parse((await reader.ReadLineAsync(Ct).AsTask().WaitAsync(Timeout, Ct))!).RootElement;

    [Fact]
    public async Task Raw_ValidRequest_GetsJsonRpcResponseWithSameId()
    {
        var (pipe, reader) = await OpenRawAsync();
        await using var _ = pipe;

        await SendLineAsync(pipe, """{"jsonrpc":"2.0","id":"abc","method":"api.hello","params":{"clientName":"raw"}}""");
        var response = await ReadLineAsync(reader);

        Assert.Equal("2.0", response.GetProperty("jsonrpc").GetString());
        Assert.Equal("abc", response.GetProperty("id").GetString());
        Assert.Equal(ApiProtocol.Version, response.GetProperty("result").GetProperty("apiVersion").GetInt32());
    }

    [Fact]
    public async Task Raw_InvalidJson_GetsParseErrorWithNullId_AndConnectionStaysUsable()
    {
        var (pipe, reader) = await OpenRawAsync();
        await using var _ = pipe;

        await SendLineAsync(pipe, "{not json");
        var error = await ReadLineAsync(reader);
        await SendLineAsync(pipe, """{"jsonrpc":"2.0","id":7,"method":"status.get"}""");
        var ok = await ReadLineAsync(reader);

        Assert.Equal(JsonValueKind.Null, error.GetProperty("id").ValueKind);
        Assert.Equal(ApiErrorCodes.ParseError, error.GetProperty("error").GetProperty("code").GetInt32());
        Assert.Equal(7, ok.GetProperty("id").GetInt32());
        Assert.Equal(JsonValueKind.Object, ok.GetProperty("result").ValueKind);
    }

    [Fact]
    public async Task Raw_TooLongLine_GetsParseError_AndConnectionStaysUsable()
    {
        var (pipe, reader) = await OpenRawAsync();
        await using var _ = pipe;

        await SendLineAsync(pipe, "{\"x\":\"" + new string('a', ApiProtocol.MaxMessageBytes + 100) + "\"}");
        var error = await ReadLineAsync(reader);
        await SendLineAsync(pipe, """{"jsonrpc":"2.0","id":8,"method":"status.get"}""");
        var ok = await ReadLineAsync(reader);

        Assert.Equal(ApiErrorCodes.ParseError, error.GetProperty("error").GetProperty("code").GetInt32());
        Assert.Equal(8, ok.GetProperty("id").GetInt32());
    }

    [Fact]
    public async Task Raw_WrongJsonRpcVersion_IsInvalidRequest()
    {
        var (pipe, reader) = await OpenRawAsync();
        await using var _ = pipe;

        await SendLineAsync(pipe, """{"jsonrpc":"1.0","id":1,"method":"status.get"}""");
        var error = await ReadLineAsync(reader);

        Assert.Equal(ApiErrorCodes.InvalidRequest, error.GetProperty("error").GetProperty("code").GetInt32());
    }

    [Fact]
    public async Task Raw_NotificationFromClient_GetsNoResponse()
    {
        var (pipe, reader) = await OpenRawAsync();
        await using var _ = pipe;

        await SendLineAsync(pipe, """{"jsonrpc":"2.0","method":"workspace.stop"}""");
        await SendLineAsync(pipe, """{"jsonrpc":"2.0","id":2,"method":"status.get"}""");
        var next = await ReadLineAsync(reader);

        Assert.Equal(2, next.GetProperty("id").GetInt32());
        Assert.DoesNotContain("stop", _handler.Calls);
    }
}
