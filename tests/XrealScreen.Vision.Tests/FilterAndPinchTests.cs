using XrealScreen.Vision.Hands;

namespace XrealScreen.Vision.Tests;

public class FilterAndPinchTests
{
    private const double Frame = 1.0 / 15;

    [Fact]
    public void OneEuro_ConstantInput_StaysConstant()
    {
        var f = new OneEuroFilter();
        for (int i = 0; i < 30; i++)
        {
            Assert.Equal(10f, f.Filter(10f, i * Frame), 4);
        }
    }

    [Fact]
    public void OneEuro_Step_MovesMonotonicallyAndConverges()
    {
        var f = new OneEuroFilter(minCutoffHz: 1.5f, beta: 0.02f);
        f.Filter(0, 0);
        float last = 0;
        for (int i = 1; i <= 45; i++)
        {
            float v = f.Filter(100, i * Frame);
            Assert.True(v >= last && v <= 100);
            last = v;
        }

        Assert.True(last > 99, $"after 3 s the filter is at {last}");
    }

    [Fact]
    public void OneEuro_ReducesJitterOfAStillValue()
    {
        var f = new OneEuroFilter(minCutoffHz: 1.5f, beta: 0.02f);
        var rnd = new Random(1);
        float rawSpread = 0, filteredSpread = 0, lastRaw = 0, lastFiltered = 0;
        for (int i = 0; i < 150; i++)
        {
            float raw = 100 + (float)(rnd.NextDouble() - 0.5) * 4;
            float filtered = f.Filter(raw, i * Frame);
            if (i > 15)
            {
                rawSpread += MathF.Abs(raw - lastRaw);
                filteredSpread += MathF.Abs(filtered - lastFiltered);
            }

            lastRaw = raw;
            lastFiltered = filtered;
        }

        Assert.True(filteredSpread < rawSpread / 2, $"jitter {filteredSpread} vs raw {rawSpread}");
    }

    [Fact]
    public void Pinch_UsesHysteresis()
    {
        var p = new PinchDetector();

        Assert.Equal(PinchEvent.None, p.Update(1.0f, 0));
        Assert.Equal(PinchEvent.None, p.Update(0.3f, Frame));        // between thresholds: still open
        Assert.Equal(PinchEvent.Pressed, p.Update(0.2f, 2 * Frame));
        Assert.Equal(PinchEvent.None, p.Update(0.4f, 3 * Frame));    // between thresholds: still pressed
        Assert.True(p.IsPressed);
        Assert.Equal(PinchEvent.None, p.Update(0.8f, 4 * Frame));    // open, but not yet for 150 ms
        Assert.Equal(PinchEvent.None, p.Update(0.8f, 6 * Frame));
        Assert.Equal(PinchEvent.Released, p.Update(0.8f, 7 * Frame));
        Assert.False(p.IsPressed);
    }

    [Fact]
    public void Pinch_SingleOpenFrameWhilePinched_DoesNotRelease()
    {
        // live test 2026-10-31: a closed pinch jumped to 0.6 for one frame and gave press-release-press
        var p = new PinchDetector();
        float[] ratios = [1.0f, 0.2f, 0.1f, 0.62f, 0.1f, 0.15f, 0.62f, 0.12f, 0.9f, 0.9f, 0.9f, 0.9f];
        var events = ratios.Select((r, i) => p.Update(r, i * Frame)).Where(e => e != PinchEvent.None).ToList();

        Assert.Equal([PinchEvent.Pressed, PinchEvent.Released], events);
    }

    [Fact]
    public void Pinch_SmallOpeningAboveTheClosedLevel_Releases()
    {
        // pointing pose (E3 test 2026-11-01): closed ~0.08, "open" only ~0.3, never above 0.45
        var p = new PinchDetector();
        float[] ratios = [0.3f, 0.08f, 0.06f, 0.1f, 0.3f, 0.32f, 0.31f, 0.3f, 0.12f, 0.3f];
        var events = ratios.Select((r, i) => p.Update(r, i * Frame)).Where(e => e != PinchEvent.None).ToList();

        Assert.Equal([PinchEvent.Pressed, PinchEvent.Released, PinchEvent.Pressed], events);
    }

    [Fact]
    public void Pinch_MinHold_NeedsTheRatioClosedLongEnough()
    {
        var p = new PinchDetector { MinHold = TimeSpan.FromMilliseconds(100) };

        Assert.Equal(PinchEvent.None, p.Update(0.2f, 0));
        Assert.Equal(PinchEvent.None, p.Update(0.9f, Frame));          // a one-frame dip is ignored
        Assert.Equal(PinchEvent.None, p.Update(0.2f, 2 * Frame));
        Assert.Equal(PinchEvent.Pressed, p.Update(0.2f, 4 * Frame));   // 133 ms closed
    }

    [Fact]
    public void Pinch_LostHand_ReleasesAfterTimeout()
    {
        var p = new PinchDetector();
        p.Update(0.1f, 0);

        Assert.Equal(PinchEvent.None, p.Update(float.NaN, 0.1));
        Assert.True(p.IsPressed);
        Assert.Equal(PinchEvent.Released, p.Update(float.NaN, 0.4));
    }

    [Fact]
    public void HandResult_PinchRatio_IsTipDistanceOverHandSize()
    {
        var hand = HandRoiTests.UprightHand(cx: 100, wristY: 200, scale: 50);
        hand[HandJoint.ThumbTip] = new(100, 100, 0);
        hand[HandJoint.IndexTip] = new(110, 100, 0);
        var result = new HandResult(hand, 1, 1, default, false, 0);

        Assert.Equal(50, result.Scale, 3);
        Assert.Equal(0.2f, result.PinchRatio, 3);
        Assert.Equal((105f, 100f), result.PinchPoint);
    }
}
