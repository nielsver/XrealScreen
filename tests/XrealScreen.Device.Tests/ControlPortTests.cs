using System.Net;
using System.Net.Sockets;
using XrealScreen.Device.XrealOne;

namespace XrealScreen.Device.Tests;

public class ControlPortTests
{
    [Fact]
    public void EncodesGetRequestLikeTheGlassesExpect()
    {
        var bytes = ControlWire.EncodeRequest(ControlCommand.GetSoftwareVersion, 1, ControlWire.GetPropertyBody);

        Assert.Equal(Convert.FromHexString("271D" + "00000006" + "80000001" + "1800"), bytes);
    }

    [Fact]
    public void ParsesStringReplyCapturedFromThe1S()
    {
        // Body of the 1S reply to GET software version, 2026-10-29.
        var body = ControlWire.EncodeStringReplyBody("1.9.1.20260914175625");
        Assert.Equal(Convert.FromHexString("2216" + "1214"), body.AsSpan(0, 4).ToArray());

        Assert.Equal("1.9.1.20260914175625", ControlWire.ParseString(body));
    }

    [Fact]
    public void ParsesLongReplyWithMultiByteLengths()
    {
        string json = "{\"x\":\"" + new string('a', 220_000) + "\"}";

        Assert.Equal(json, ControlWire.ParseString(ControlWire.EncodeStringReplyBody(json)));
    }

    [Fact]
    public void RejectedRequestThrowsWithStatus()
    {
        // 22 02 [08 05] = status 5 instead of a string.
        var ex = Assert.Throws<InvalidDataException>(() => ControlWire.ParseString([0x22, 0x02, 0x08, 0x05]));
        Assert.Contains("status 5", ex.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(127)]
    [InlineData(128)]
    [InlineData(220_169)]
    public void VarintRoundTrips(int value)
    {
        var bytes = ControlWire.EncodeVarint(value);
        int offset = 0;
        Assert.Equal(value, ControlWire.ReadVarint(bytes, ref offset));
        Assert.Equal(bytes.Length, offset);
    }

    [Fact]
    public async Task ClientSkipsEventsAndMatchesTransaction()
    {
        var ct = TestContext.Current.CancellationToken;
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;

        var server = Task.Run(async () =>
        {
            using var socket = await listener.AcceptTcpClientAsync(ct);
            await using var stream = socket.GetStream();
            for (int i = 0; i < 2; i++)
            {
                var request = await ControlWire.ReadAsync(stream, 1024, ct);
                Assert.NotNull(request);
                // An unsolicited key event and a reply to another transaction come first.
                await stream.WriteAsync(ControlWire.EncodeReply(0x272E, 0, [0x08, 0x01]), ct);
                await stream.WriteAsync(ControlWire.EncodeReply(request.Value.Magic, request.Value.TransactionId + 100, ControlWire.EncodeStringReplyBody("wrong")), ct);
                string value = request.Value.Magic == ControlCommand.GetDspVersion ? "15.A.01.097_20250430" : "1.9.1.20260914175625";
                await stream.WriteAsync(ControlWire.EncodeReply(request.Value.Magic, request.Value.TransactionId, ControlWire.EncodeStringReplyBody(value)), ct);
            }
        }, ct);

        await using var client = await XrealControlClient.ConnectAsync(new OneEndpoints { Host = "127.0.0.1", ControlPort = port }, TimeSpan.FromSeconds(5), ct);
        Assert.Equal("1.9.1.20260914175625", await client.GetSoftwareVersionAsync(TimeSpan.FromSeconds(5), ct));
        Assert.Equal("15.A.01.097_20250430", await client.GetDspVersionAsync(TimeSpan.FromSeconds(5), ct));
        await server;
    }

    [Fact]
    public async Task ClientTimesOutWhenNoReply()
    {
        var ct = TestContext.Current.CancellationToken;
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var accept = listener.AcceptTcpClientAsync(ct).AsTask();

        await using var client = await XrealControlClient.ConnectAsync(new OneEndpoints { Host = "127.0.0.1", ControlPort = port }, TimeSpan.FromSeconds(5), ct);
        await Assert.ThrowsAsync<TimeoutException>(() => client.GetSoftwareVersionAsync(TimeSpan.FromMilliseconds(200), ct));
        (await accept).Dispose();
    }

    [Fact]
    public void ParsesCalibrationCameras()
    {
        // Shape and camera values of the 1S + Eye calibration (2026-10-29), serial omitted.
        const string json = """
            {"FSN":"X","glasses_version":7,"last_modified_time":"2026-04-19 12:31:18",
             "RGB_camera":{"device_1":{"cc":[1007.46,758.89],"fc":[960.13,960.06],"resolution":[2016,1512]},"num_of_cameras":1},
             "SLAM_camera":{"device_1":{"cc":[251.87,189.72],"fc":[240.03,240.02],"resolution":[504.0,378.0]},"num_of_cameras":1}}
            """;

        var c = GlassesCalibration.Parse(json);

        Assert.Equal(7, c.GlassesVersion);
        Assert.Equal("2026-04-19 12:31:18", c.LastModified);
        Assert.Equal((2016, 1512), (c.RgbCamera!.Width, c.RgbCamera.Height));
        Assert.Equal((504, 378), (c.SlamCamera!.Width, c.SlamCamera.Height));
        Assert.InRange(c.SlamCamera.HorizontalFovDegrees, 92, 94);
        Assert.InRange(c.SlamCamera.VerticalFovDegrees, 75, 77);
    }

    [Fact]
    public void CalibrationWithoutCamerasParses()
    {
        var c = GlassesCalibration.Parse("""{"glasses_version":7}""");
        Assert.Null(c.RgbCamera);
        Assert.Null(c.SlamCamera);
    }
}
