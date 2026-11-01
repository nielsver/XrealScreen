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
    public void Calibration_Inverse_UndoesMap()
    {
        var c = new HandCalibration(0.004, 0.0005, -0.3, -0.0004, 0.005, -0.2);

        var (x, y) = c.Inverse(0.3, 0.7);
        var (u, v) = c.Map(x, y);

        Assert.Equal(0.3, u, 9);
        Assert.Equal(0.7, v, 9);
    }

    [Fact]
    public void Calibration_ScreenCornerOutsideTheCamera_IsDetected()
    {
        Assert.True(HandCalibration.Default(504, 378).CoversScreenInsideCamera(504, 378, margin: 20));

        // the screen's left edge needs the hand at x < 0: the user cannot reach it (the 2026-11-01 failure)
        var skewed = new HandCalibration(1.0 / 300, 0, 0.2, 0, 1.0 / 200, -0.4);
        Assert.False(skewed.CoversScreenInsideCamera(504, 378, margin: 20));
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
        var pointer = new HandPointer(Identity(), PinchOnly);

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
        var pointer = new HandPointer(Identity(), PinchOnly);
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
        var pointer = new HandPointer(Identity(), PinchOnly);
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
        var pointer = new HandPointer(Identity(), PinchOnly);
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
        var pointer = new HandPointer(Identity(), PinchOnly);
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

    [Fact]
    public void Dwell_HoldingStill_ClicksOnce_ThenNeedsAMoveToRearm()
    {
        var pointer = new HandPointer(Identity(), new HandPointerOptions { Click = ClickMode.Dwell });
        var actions = new List<PointerAction>();
        double t = 0, lastProgress = 0;
        for (int i = 0; i < 40; i++, t += Frame)
        {
            var f = pointer.Update(Hand(0.4f, 0.4f, 1.0f), t);
            actions.Add(f.Action);
            if (f.Action == PointerAction.None && f.DwellProgress > 0)
            {
                Assert.True(f.DwellProgress >= lastProgress);
                lastProgress = f.DwellProgress;
            }
        }

        Assert.Equal([PointerAction.ButtonDown, PointerAction.ButtonUp], actions.Where(a => a != PointerAction.None));

        // move away and hold still again: a second click
        for (int i = 0; i < 40; i++, t += Frame)
        {
            actions.Add(pointer.Update(Hand(0.6f, 0.4f, 1.0f), t).Action);
        }

        Assert.Equal(2, actions.Count(a => a == PointerAction.ButtonDown));
    }

    [Fact]
    public void Dwell_MovingPointer_DoesNotClick()
    {
        var pointer = new HandPointer(Identity(), new HandPointerOptions { Click = ClickMode.Dwell });

        for (int i = 0; i < 60; i++)
        {
            var f = pointer.Update(Hand(0.2f + 0.01f * i, 0.4f, 1.0f), i * Frame);
            Assert.Equal(PointerAction.None, f.Action);
        }
    }

    [Fact]
    public void Dwell_InPinchMode_NeverClicks()
    {
        var pointer = new HandPointer(Identity(), PinchOnly);

        for (int i = 0; i < 60; i++)
        {
            Assert.Equal(PointerAction.None, pointer.Update(Hand(0.4f, 0.4f, 1.0f), i * Frame).Action);
        }
    }

    [Fact]
    public void PinchRightAfterAClick_IsIgnored_UntilTheRepeatGuardHasPassed()
    {
        var pointer = new HandPointer(Identity(), PinchOnly);
        var downs = new List<double>();
        double t = 0;
        // pinch, open, pinch again 0.4 s later (a bounce), then a real pinch 1.5 s later
        foreach (var (pinch, frames) in new[] { (1.0f, 10), (0.1f, 4), (1.0f, 4), (0.1f, 4), (1.0f, 22), (0.1f, 4), (1.0f, 4) })
        {
            for (int i = 0; i < frames; i++, t += Frame)
            {
                if (pointer.Update(Hand(0.5f, 0.5f, pinch), t).Action == PointerAction.ButtonDown)
                {
                    downs.Add(t);
                }
            }
        }

        Assert.Equal(2, downs.Count);
        Assert.False(pointer.IsButtonDown);
    }

    [Fact]
    public void HoldingStill_AfterAPinchClick_DoesNotDwellClickAgain()
    {
        var pointer = new HandPointer(Identity());
        double t = 0;
        int downs = 0;
        foreach (var (pinch, frames) in new[] { (1.0f, 5), (0.1f, 4), (1.0f, 60) })
        {
            for (int i = 0; i < frames; i++, t += Frame)
            {
                downs += pointer.Update(Hand(0.5f, 0.5f, pinch), t).Action == PointerAction.ButtonDown ? 1 : 0;
            }
        }

        Assert.Equal(1, downs);
    }

    [Fact]
    public void DwellMode_IgnoresPinches_AndStillDwellsWithAHandThatLooksPinched()
    {
        var pointer = new HandPointer(Identity(), new HandPointerOptions { Click = ClickMode.Dwell });
        var actions = new List<PointerAction>();
        for (int i = 0; i < 20; i++)
        {
            // relaxed hand: the camera reads thumb and index as closed
            actions.Add(pointer.Update(Hand(0.4f, 0.4f, 0.1f), i * Frame).Action);
        }

        Assert.Equal([PointerAction.ButtonDown, PointerAction.ButtonUp], actions.Where(a => a != PointerAction.None));
    }

    [Fact]
    public void PrecisionZone_SlowDrift_MovesThePointerLessThanTheHand()
    {
        var pointer = new HandPointer(Identity(), PinchOnly);
        PointerFrame first = default, last = default;
        for (int i = 0; i < 30; i++)
        {
            // 0.03 screen/s: a "still" hand drifting
            last = pointer.Update(Hand(0.5f + 0.002f * i, 0.5f, 1.0f), i * Frame);
            if (i == 0)
            {
                first = last;
            }
        }

        double handMoved = 0.002 * 29;
        Assert.InRange(last.U - first.U, 0.2 * handMoved, 0.5 * handMoved);
    }

    [Fact]
    public void PrecisionZone_FastMove_CatchesUpWithTheHand()
    {
        var pointer = new HandPointer(Identity(), PinchOnly);
        double t = 0;
        for (int i = 0; i < 30; i++, t += Frame)
        {
            pointer.Update(Hand(0.5f + 0.002f * i, 0.5f, 1.0f), t);   // slow drift builds an offset
        }

        PointerFrame f = default;
        for (int i = 0; i < 8; i++, t += Frame)
        {
            f = pointer.Update(Hand(0.56f + 0.04f * i, 0.5f, 1.0f), t); // fast sweep to 0.84
        }

        for (int i = 0; i < 20; i++, t += Frame)
        {
            f = pointer.Update(Hand(0.84f, 0.5f, 1.0f), t);
        }

        Assert.InRange(f.U, 0.78, 0.86);
    }

    private static readonly HandPointerOptions PinchOnly = new() { Click = ClickMode.Pinch };

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
