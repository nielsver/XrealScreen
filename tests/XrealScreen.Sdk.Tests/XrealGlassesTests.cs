using System.Runtime.CompilerServices;
using XrealScreen.Core.Abstractions;
using XrealScreen.Core.Tracking;
using XrealScreen.Device.Simulated;

namespace XrealScreen.Sdk.Tests;

public class XrealGlassesTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    private static XrealGlasses TurningLeft(float degreesPerSecond = 30f, double seconds = 2) =>
        new(new SyntheticImuSource(new SyntheticImuOptions { Motion = SyntheticMotion.ConstantYaw, AmplitudeDegrees = degreesPerSecond, DurationSeconds = seconds }), TimeSpan.FromMilliseconds(10));

    private static async Task<List<GlassesPose>> TakeAsync(XrealGlasses glasses, int count, Action<int>? onPose = null)
    {
        using var cts = new CancellationTokenSource(Timeout);
        var poses = new List<GlassesPose>();
        await foreach (var pose in glasses.ReadPosesAsync(cts.Token))
        {
            poses.Add(pose);
            onPose?.Invoke(poses.Count);
            if (poses.Count == count)
            {
                break;
            }
        }

        return poses;
    }

    [Fact]
    public async Task ReadPoses_ConstantLeftTurn_YawGrowsPositiveAtTheTurnRate()
    {
        var glasses = TurningLeft(30f);

        var poses = await TakeAsync(glasses, 1001); // 1 kHz: 1 s after the first pose

        Assert.InRange(poses[^1].Yaw - poses[0].Yaw, 29f, 31f);
        Assert.InRange(poses[^1].AngularVelocity.Z, 29f, 31f);
        Assert.InRange(MathF.Abs(poses[^1].Pitch), 0f, 0.5f);
        Assert.Equal(poses[^1], glasses.LatestPose);
        Assert.Equal(1001, glasses.SampleCount);
    }

    [Fact]
    public async Task Recenter_NextPoseIsStraightAhead()
    {
        var glasses = TurningLeft(30f);

        var poses = await TakeAsync(glasses, 700, n =>
        {
            if (n == 500)
            {
                glasses.Recenter();
            }
        });

        Assert.True(poses[499].Yaw > 10f);
        Assert.InRange(poses[500].Yaw, -0.5f, 0.5f);
        Assert.InRange(poses[699].Yaw, 5.5f, 6.5f); // 200 samples at 30 °/s
    }

    [Fact]
    public async Task ReadPoses_SourceFailsTwice_ReportsOneWaitThenConnects()
    {
        var source = new FlakySource(failures: 2);
        var glasses = new XrealGlasses(source, TimeSpan.FromMilliseconds(5));
        var events = new List<GlassesConnectionChangedEventArgs>();
        glasses.ConnectionChanged += (_, e) => events.Add(e);

        var poses = await TakeAsync(glasses, 10);

        Assert.Equal(10, poses.Count);
        Assert.Equal(3, source.Attempts);
        Assert.Collection(
            events,
            e => Assert.False(e.IsConnected),                 // first failure, reported once
            e => Assert.True(e.IsConnected),
            e => Assert.False(e.IsConnected));                // enumeration ended (break)
        Assert.Contains("not reachable", events[0].Error, StringComparison.Ordinal);
        Assert.False(glasses.IsConnected);
    }

    [Fact]
    public async Task ReadPoses_StreamEnds_ReconnectsAndContinues()
    {
        var glasses = TurningLeft(seconds: 0.05); // 50 samples per connection
        int disconnects = 0;
        glasses.ConnectionChanged += (_, e) => disconnects += e.IsConnected ? 0 : 1;

        var poses = await TakeAsync(glasses, 120);

        Assert.Equal(120, poses.Count);
        Assert.True(disconnects >= 2, $"disconnects={disconnects}");
    }

    [Fact]
    public async Task ReadPoses_SecondConcurrentReader_Throws()
    {
        var glasses = TurningLeft();
        using var cts = new CancellationTokenSource(Timeout);
        await using var first = glasses.ReadPosesAsync(cts.Token).GetAsyncEnumerator(cts.Token);
        Assert.True(await first.MoveNextAsync());

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await foreach (var _ in glasses.ReadPosesAsync(cts.Token))
            {
            }
        });
    }

    [Fact]
    public async Task ReadPoses_AfterFirstReaderEnds_CanReadAgain()
    {
        var glasses = TurningLeft();
        await TakeAsync(glasses, 5);

        var again = await TakeAsync(glasses, 5);

        Assert.Equal(5, again.Count);
    }

    [Fact]
    public void LatestPose_BeforeAnySample_IsNull()
    {
        Assert.Null(TurningLeft().LatestPose);
    }

    /// <summary>Fails <c>failures</c> times like unreachable glasses, then streams still samples.</summary>
    private sealed class FlakySource(int failures) : IImuSource
    {
        public int Attempts { get; private set; }

        public string Name => "flaky";

        public async IAsyncEnumerable<ImuSample> ReadAsync([EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await Task.Yield();
            if (++Attempts <= failures)
            {
                throw new IOException("glasses not reachable");
            }

            for (long i = 0; !cancellationToken.IsCancellationRequested; i++)
            {
                yield return new ImuSample(i * 1_000_000, System.Numerics.Vector3.Zero, new System.Numerics.Vector3(0, 0, 9.81f));
            }
        }
    }
}
