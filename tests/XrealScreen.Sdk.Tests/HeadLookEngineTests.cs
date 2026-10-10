using XrealScreen.Core.Gaming;
using XrealScreen.Device.Simulated;

namespace XrealScreen.Sdk.Tests;

public class HeadLookEngineTests
{
    /// <summary>Runs the engine over one second of a constant left turn and returns what reached the mouse.</summary>
    private static async Task<RecordingMouse> RunOneSecondAsync(bool enabled, bool accept = true, HeadMouseSettings? settings = null)
    {
        var glasses = new XrealGlasses(
            // Real-time pacing: the engine sends at most every 2 ms (wall clock), like with real glasses.
            new SyntheticImuSource(new SyntheticImuOptions { Motion = SyntheticMotion.ConstantYaw, AmplitudeDegrees = 30f, DurationSeconds = 1, RealTime = true }),
            TimeSpan.FromSeconds(30));
        var mouse = new RecordingMouse(accept);
        var engine = new HeadLookEngine(glasses, mouse) { PollHotkeys = false, Settings = settings ?? new HeadMouseSettings { CountsPerDegree = 10 }, Enabled = enabled };

        // The finite source ends after one second; stop as soon as the glasses report the end.
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        glasses.ConnectionChanged += (_, e) =>
        {
            if (!e.IsConnected)
            {
                cts.Cancel();
            }
        };
        await engine.RunAsync(cts.Token);
        mouse.Sends = engine.Sends;
        mouse.Rejected = engine.Rejected;
        return mouse;
    }

    [Fact]
    public async Task Enabled_LeftTurn_MovesMouseLeftByCountsPerDegree()
    {
        var mouse = await RunOneSecondAsync(enabled: true);

        // 30 °/s × 1 s × 10 counts/° = 300 counts to the left; the 2 ms send interval batches them.
        Assert.InRange(mouse.TotalX, -302, -290);
        Assert.InRange(mouse.TotalY, -2, 2);
        Assert.True(mouse.Moves.Count <= 501, $"moves={mouse.Moves.Count}");
        Assert.Equal(mouse.Moves.Count, mouse.Sends);
    }

    [Fact]
    public async Task Disabled_SendsNothing()
    {
        var mouse = await RunOneSecondAsync(enabled: false);

        Assert.Empty(mouse.Moves);
    }

    [Fact]
    public async Task RefusedMoves_AreCountedAsRejected()
    {
        var mouse = await RunOneSecondAsync(enabled: true, accept: false);

        Assert.Equal(0, mouse.Sends);
        Assert.Equal(mouse.Moves.Count, mouse.Rejected);
        Assert.NotEmpty(mouse.Moves);
    }

    [Fact]
    public async Task JoystickMode_StraightAheadTurn_UsesTheSettings()
    {
        // Joystick: the head passes 5° after 1/6 s, full speed (25°) is never reached in 1 s (30°).
        var mouse = await RunOneSecondAsync(enabled: true, settings: new HeadMouseSettings { Mode = HeadMouseMode.Stick, StickDeadZoneDegrees = 5, StickFullDegrees = 35, StickMaxCountsPerSecond = 600 });

        Assert.True(mouse.TotalX < -50, $"x={mouse.TotalX}");
        Assert.True(mouse.TotalX > -600, $"x={mouse.TotalX}");
    }

    [Fact]
    public void Enabled_RaisesEnabledChangedOnlyOnChange()
    {
        var engine = new HeadLookEngine(new XrealGlasses(new SyntheticImuSource()), new RecordingMouse(true)) { PollHotkeys = false };
        var changes = new List<bool>();
        engine.EnabledChanged += changes.Add;

        engine.Enabled = true;
        engine.Enabled = true;
        engine.Enabled = false;

        Assert.Equal([true, false], changes);
    }

    private sealed class RecordingMouse(bool accept) : IMouseOutput
    {
        public List<MouseDelta> Moves { get; } = [];

        public int TotalX => Moves.Sum(m => m.X);

        public int TotalY => Moves.Sum(m => m.Y);

        public long Sends { get; set; }

        public long Rejected { get; set; }

        public bool Move(MouseDelta delta)
        {
            Moves.Add(delta);
            return accept;
        }
    }
}
