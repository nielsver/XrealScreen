using XrealScreen.Core.Abstractions;
using XrealScreen.Device.Simulated;
using XrealScreen.Host.Hud;
using XrealScreen.Render.Scene;

namespace XrealScreen.Host.Tests;

public class ArHudEngineTests
{
    [Fact]
    public async Task SimulatedSession_RunsAndStopsWithoutADisplay()
    {
        var imu = new SyntheticImuSource(new SyntheticImuOptions
        {
            Motion = SyntheticMotion.Still,
            DurationSeconds = 0.3,
            RateHz = 200,
            RealTime = false,
        });
        var output = new FakeHudOutput();
        await using var engine = new ArHudEngine(imu, output);
        var ct = TestContext.Current.CancellationToken;

        await engine.StartAsync(ct);

        Assert.Equal(ArHudState.Running, engine.State);
        Assert.True(output.Started);
        var until = DateTime.UtcNow.AddSeconds(2);
        while (engine.ImuSamples == 0 && DateTime.UtcNow < until)
        {
            await Task.Delay(20, ct);
        }

        Assert.True(engine.ImuSamples > 0);
        output.RaiseRecenter();
        int? selected = null;
        engine.PinSelected += id => selected = id;
        output.RaisePin(2);
        Assert.Equal(2, selected);

        await engine.StopAsync();

        Assert.Equal(ArHudState.Stopped, engine.State);
        Assert.True(output.Stopped);
    }

    private sealed class FakeHudOutput : IHudOutput
    {
        private readonly TaskCompletionSource _done = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public bool Started { get; private set; }

        public bool Stopped { get; private set; }

        public Task Completion => _done.Task;

        public event Action? RecenterRequested;

        public event Action<int>? PinSelected;

        public void Start(Func<HeadView> latchPose)
        {
            ArgumentNullException.ThrowIfNull(latchPose);
            Started = true;
        }

        public void RequestStop()
        {
            Stopped = true;
            _done.TrySetResult();
        }

        public void RaiseRecenter() => RecenterRequested?.Invoke();

        public void RaisePin(int id) => PinSelected?.Invoke(id);

        public void Dispose() => RequestStop();
    }
}
