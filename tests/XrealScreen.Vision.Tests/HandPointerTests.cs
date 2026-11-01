using XrealScreen.Vision.Hands;

namespace XrealScreen.Vision.Tests;

public class HandPointerTests
{
    private const double Frame = 1.0 / 15;

    [Fact]
    public void Calibration_Default_MapsTheMiddleOfTheCameraToTheWholeScreen()
    {
        var c = HandCalibration.Default(504, 378);

        var (u0, v0) = c.Map(0.25 * 504, 0.20 * 378);
        var (u1, v1) = c.Map(0.75 * 504, 0.70 * 378);

        Assert.Equal(0, u0, 6);
        Assert.Equal(0, v0, 6);
        Assert.Equal(1, u1, 6);
        Assert.Equal(1, v1, 6);
    }

    [Fact]
    public void Calibration_Fit_RecoversAnAffineMapping()
    {
        var truth = new HandCalibration(0.004, 0.0005, -0.3, -0.0004, 0.005, -0.2);
        var points = new[] { (150.0, 100.0), (350, 100), (150, 280), (350, 280), (250, 190) }
            .Select(p => { var (u, v) = truth.Map(p.Item1, p.Item2); return new CalibrationPoint(p.Item1, p.Item2, u, v); })
            .ToList();

        var fit = HandCalibration.Fit(points);

        Assert.Equal(truth.A, fit.A, 9);
        Assert.Equal(truth.B, fit.B, 9);
        Assert.Equal(truth.C, fit.C, 9);
        Assert.Equal(truth.D, fit.D, 9);
        Assert.Equal(truth.E, fit.E, 9);
        Assert.Equal(truth.F, fit.F, 9);
        Assert.True(fit.RmsError(points) < 1e-9);
    }

    [Fact]
    public void Calibration_Fit_PointsOnALine_Throws()
    {
        var points = new[] { new CalibrationPoint(0, 0, 0, 0), new CalibrationPoint(1, 1, 0.5, 0.5), new CalibrationPoint(2, 2, 1, 1) };

        Assert.Throws<ArgumentException>(() => HandCalibration.Fit(points));
    }

    [Fact]
    public void Pointer_FollowsTheIndexBaseThroughTheCalibration()
    {
        var pointer = new HandPointer(Identity());

        PointerFrame f = default;
        for (int i = 0; i < 30; i++)
        {
            f = pointer.Update(Hand(indexBaseX: 0.3f, indexBaseY: 0.6f, pinch: 1.0f), i * Frame);
        }

        Assert.True(f.HasPointer);
        Assert.Equal(0.3, f.U, 2);
        Assert.Equal(0.6, f.V, 2);
    }

    [Fact]
    public void Pinch_GivesButtonDownAndUp_AndFreezesThePointerAroundTheClick()
    {
        var pointer = new HandPointer(Identity());
        double t = 0;
        for (int i = 0; i < 20; i++, t += Frame)
        {
            pointer.Update(Hand(0.5f, 0.5f, 1.0f), t);
        }

        var down = pointer.Update(Hand(0.5f, 0.5f, 0.1f), t);
        t += Frame;
        var moved = pointer.Update(Hand(0.6f, 0.5f, 0.1f), t);   // hand twitches while pinching
        t += Frame;

        Assert.Equal(PointerAction.ButtonDown, down.Action);
        Assert.Equal(0.5, moved.U, 2);                          // frozen for 200 ms
        Assert.True(pointer.IsButtonDown);

        PointerFrame up = default;
        for (int i = 0; i < 4 && up.Action != PointerAction.ButtonUp; i++, t += Frame)
        {
            up = pointer.Update(Hand(0.6f, 0.5f, 1.0f), t);
        }

        Assert.Equal(PointerAction.ButtonUp, up.Action);
        Assert.False(pointer.IsButtonDown);
    }

    [Fact]
    public void HeldPinch_Drags_AfterTheFreeze()
    {
        var pointer = new HandPointer(Identity());
        double t = 0;
        for (int i = 0; i < 20; i++, t += Frame)
        {
            pointer.Update(Hand(0.2f, 0.5f, 1.0f), t);
        }

        PointerFrame f = default;
        for (int i = 0; i < 30; i++, t += Frame)
        {
            f = pointer.Update(Hand(0.2f + 0.02f * i, 0.5f, 0.1f), t);
        }

        Assert.True(pointer.IsButtonDown);
        Assert.True(f.U > 0.6, $"pointer at {f.U} while dragging");
    }

    [Fact]
    public void LostHand_WhilePressed_ReleasesTheButton()
    {
        var pointer = new HandPointer(Identity());
        double t = 0;
        pointer.Update(Hand(0.5f, 0.5f, 1.0f), t);
        pointer.Update(Hand(0.5f, 0.5f, 0.1f), t += Frame);

        var lost = pointer.Update(null, t += 0.1);
        var stillLost = pointer.Update(null, t += 0.3);

        Assert.False(lost.HasPointer);
        Assert.Equal(PointerAction.None, lost.Action);
        Assert.Equal(PointerAction.ButtonUp, stillLost.Action);
    }

    [Fact]
    public void MiddlePinch_MovedUp_Scrolls_WithoutClicking()
    {
        var pointer = new HandPointer(Identity());
        double t = 0;
        for (int i = 0; i < 10; i++, t += Frame)
        {
            pointer.Update(Hand(0.5f, 0.5f, 1.0f), t);
        }

        int notches = 0;
        for (int i = 0; i < 15; i++, t += Frame)
        {
            var f = pointer.Update(Hand(0.5f, 0.5f - 0.01f * i, 1.0f, middlePinch: 0.1f), t);
            Assert.NotEqual(PointerAction.ButtonDown, f.Action);
            notches += f.ScrollNotches;
        }

        Assert.True(pointer.IsScrolling);
        Assert.InRange(notches, 2, 4); // hand moved 0.14 up (minus smoothing lag), 0.04 per notch
    }

    /// <summary>Camera coordinates equal screen units (0..1) for easy numbers.</summary>
    private static HandCalibration Identity() => new(1, 0, 0, 0, 1, 0);

    /// <summary>A hand in "camera" units 0..1 with scale 0.1, the index base at (x, y) and the given pinch ratios.</summary>
    private static HandResult Hand(float indexBaseX, float indexBaseY, float pinch, float middlePinch = 1.0f)
    {
        const float scale = 0.1f;
        var p = new HandPoint[HandJoint.Count];
        for (int i = 0; i < p.Length; i++)
        {
            p[i] = new HandPoint(indexBaseX, indexBaseY, 0);
        }

        p[HandJoint.Wrist] = new(indexBaseX, indexBaseY + scale, 0);
        p[HandJoint.MiddleBase] = new(indexBaseX, indexBaseY, 0);
        p[HandJoint.ThumbTip] = new(indexBaseX - 0.05f, indexBaseY - 0.05f, 0);
        p[HandJoint.IndexTip] = new(indexBaseX - 0.05f + pinch * scale, indexBaseY - 0.05f, 0);
        p[HandJoint.MiddleTip] = new(indexBaseX - 0.05f, indexBaseY - 0.05f + middlePinch * scale, 0);
        return new HandResult(p, 1, 1, default, false, 0);
    }
}
