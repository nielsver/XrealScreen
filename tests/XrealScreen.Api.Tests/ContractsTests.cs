using System.Text;
using System.Text.Json;
using XrealScreen.Contracts;

namespace XrealScreen.Api.Tests;

public class ContractsTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static JsonLineChannel Channel(string text, int chunk = int.MaxValue, int max = ApiProtocol.MaxMessageBytes) =>
        new(new ChunkedStream(Encoding.UTF8.GetBytes(text), chunk), max);

    [Fact]
    public async Task Read_LinesSplitAcrossReads_AreReassembled()
    {
        await using var channel = Channel("""{"jsonrpc":"2.0","id":1,"method":"a"}""" + "\n" + """{"jsonrpc":"2.0","id":2,"method":"b"}""" + "\n", chunk: 3);

        var first = await channel.ReadAsync(Ct);
        var second = await channel.ReadAsync(Ct);
        var end = await channel.ReadAsync(Ct);

        Assert.Equal("a", first.Message!.Method);
        Assert.Equal("b", second.Message!.Method);
        Assert.Equal(ReadOutcome.EndOfStream, end.Outcome);
    }

    [Fact]
    public async Task Read_BlankAndCrLfLines_AreSkipped()
    {
        await using var channel = Channel("\r\n  \n{\"jsonrpc\":\"2.0\",\"method\":\"x\"}\r\n");

        var read = await channel.ReadAsync(Ct);

        Assert.Equal("x", read.Message!.Method);
    }

    [Fact]
    public async Task Read_TooLongLine_IsInvalid_ThenNextLineIsRead()
    {
        string longLine = "{\"x\":\"" + new string('a', 200) + "\"}";
        await using var channel = Channel(longLine + "\n{\"method\":\"next\"}\n", chunk: 7, max: 64);

        var tooLong = await channel.ReadAsync(Ct);
        var next = await channel.ReadAsync(Ct);

        Assert.Equal(ReadOutcome.Invalid, tooLong.Outcome);
        Assert.Contains("64", tooLong.Error, StringComparison.Ordinal);
        Assert.Equal("next", next.Message!.Method);
    }

    [Theory]
    [InlineData("{oops")]
    [InlineData("[1,2,3]")]
    [InlineData("null")]
    [InlineData("\"text\"")]
    public async Task Read_NotAJsonObject_IsInvalid(string line)
    {
        await using var channel = Channel(line + "\n");

        var read = await channel.ReadAsync(Ct);

        Assert.Equal(ReadOutcome.Invalid, read.Outcome);
        Assert.NotNull(read.Error);
    }

    [Fact]
    public async Task Write_OneCompactLineWithCamelCaseAndNoNulls()
    {
        var stream = new MemoryStream();
        await using var channel = new JsonLineChannel(stream);
        var pose = new PoseData { TimestampNs = 5, Yaw = 1.5f, Source = PoseSources.Workspace };

        await channel.WriteAsync(RpcMessage.Notification(ApiNotifications.Pose, JsonSerializer.SerializeToElement(pose, ApiJsonContext.Default.PoseData)), Ct);
        string text = Encoding.UTF8.GetString(stream.ToArray());

        Assert.EndsWith("\n", text, StringComparison.Ordinal);
        Assert.Single(text.Split('\n', StringSplitOptions.RemoveEmptyEntries));
        Assert.StartsWith("""{"jsonrpc":"2.0","method":"pose","params":{"timestampNs":5,"yaw":1.5,""", text, StringComparison.Ordinal);
        Assert.DoesNotContain("\"id\"", text, StringComparison.Ordinal);
        Assert.DoesNotContain("null", text, StringComparison.Ordinal);
    }

    [Fact]
    public void HeadLookSettings_OmittedMembersStayNull()
    {
        var changes = JsonSerializer.Deserialize("""{"mode":"joystick"}""", ApiJsonContext.Default.HeadLookSettings)!;

        Assert.Equal(HeadLookModes.Joystick, changes.Mode);
        Assert.Null(changes.Sensitivity);
        Assert.Null(changes.GlassesPrimary);
    }

    [Fact]
    public void MethodList_HasNoDuplicates()
    {
        Assert.Equal(ApiMethods.All.Count, ApiMethods.All.Distinct().Count());
    }

    /// <summary>Returns at most <c>chunk</c> bytes per read, like a pipe delivering partial messages.</summary>
    private sealed class ChunkedStream(byte[] data, int chunk) : Stream
    {
        private int _position;

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => data.Length;

        public override long Position { get => _position; set => throw new NotSupportedException(); }

        public override int Read(byte[] buffer, int offset, int count)
        {
            int n = Math.Min(Math.Min(count, chunk), data.Length - _position);
            Array.Copy(data, _position, buffer, offset, n);
            _position += n;
            return n;
        }

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
