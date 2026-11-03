using XrealScreen.Core.Hud;

namespace XrealScreen.Core.Tests;

public class HudLayoutTests
{
    private static HudSegment Horizon(HudFrame frame) =>
        Assert.Single(frame.Lines, line => line.Part == HudPart.Horizon);

    [Fact]
    public void Level_HorizonIsHorizontalThroughTheCenter()
    {
        var frame = HudLayout.Build(0, 0, 0, clock: null);

        var horizon = Horizon(frame);
        Assert.True(horizon.X0 < -0.2f && horizon.X1 > 0.2f);
        Assert.Equal(0, horizon.Y0, precision: 3);
        Assert.Equal(0, horizon.Y1, precision: 3);
        Assert.Contains(frame.Lines, line => line.Part == HudPart.Reticle);
    }

    [Fact]
    public void LookUp_MovesTheHorizonDown()
    {
        var level = Horizon(HudLayout.Build(0, 0, 0, clock: null));
        var up = Horizon(HudLayout.Build(0, 10, 0, clock: null));

        Assert.True(up.Y0 < level.Y0 - 0.2f);
        Assert.Equal(up.Y0, up.Y1, precision: 3);
    }

    [Fact]
    public void RightShoulderDown_RaisesTheRightEndOfTheHorizon()
    {
        var horizon = Horizon(HudLayout.Build(0, 0, 20, clock: null));
        float leftY = horizon.X0 < horizon.X1 ? horizon.Y0 : horizon.Y1;
        float rightY = horizon.X0 < horizon.X1 ? horizon.Y1 : horizon.Y0;

        Assert.True(rightY > leftY + 0.1f);
    }

    [Fact]
    public void TurnLeft_MovesTheZeroTickToTheRight()
    {
        float At(float yaw) => Assert.Single(HudLayout.Build(yaw, 0, 0, clock: null).Labels, label => label.Text == "0").X;

        Assert.Equal(0, At(0), precision: 3);
        Assert.True(At(12) > 0.2f);
    }

    [Fact]
    public void HiddenHorizon_OmitsTheHorizonLine()
    {
        var frame = HudLayout.Build(0, 0, 0, clock: null, new HudLayoutOptions { ShowHorizon = false });

        Assert.DoesNotContain(frame.Lines, line => line.Part == HudPart.Horizon);
        Assert.Contains(frame.Lines, line => line.Part == HudPart.Reticle);
    }

    [Fact]
    public void MatchingPitch_PutsThatLadderTickOnTheCenter()
    {
        var frame = HudLayout.Build(0, 10, 0, clock: null);
        var onCenter = frame.Lines.Where(line => line.Part == HudPart.Ladder && MathF.Abs(line.Y0) < 0.02f && MathF.Abs(line.Y1) < 0.02f);

        Assert.NotEmpty(onCenter);
    }

    [Fact]
    public void Markers_KeepTheCornerBracketsFixedWhenTheHeadRolls()
    {
        static bool TopLeft(HudSegment line) =>
            line.Part == HudPart.Marker && Near(line.X0, -0.90f) && Near(line.Y0, 0.82f);

        Assert.Contains(HudLayout.Build(0, 0, 0, clock: null).Lines, TopLeft);
        Assert.Contains(HudLayout.Build(30, 15, 40, clock: null).Lines, TopLeft);
    }

    [Fact]
    public void TurnLeft_MovesTheHomeDiamondToTheRight()
    {
        static float DiamondX(float yaw)
        {
            var diamond = HudLayout.Build(yaw, 0, 0, clock: null).Lines
                .Where(line => line.Part == HudPart.Marker && line.Y0 > 0.05f && line.Y0 < 0.25f && MathF.Abs(line.X0) < 0.6f)
                .ToList();
            Assert.NotEmpty(diamond);
            return diamond.Average(line => (line.X0 + line.X1) / 2f);
        }

        Assert.Equal(0, DiamondX(0), precision: 2);
        Assert.True(DiamondX(12) > 0.2f);
    }

    [Fact]
    public void LookFarUp_ReplacesTheDiamondWithAnEdgeChevron()
    {
        var lines = HudLayout.Build(0, 80, 0, clock: null).Lines.Where(line => line.Part == HudPart.Marker).ToList();

        Assert.Contains(lines, line => line.Y0 < -0.7f && MathF.Abs(line.X0) < 0.15f);
        Assert.DoesNotContain(lines, line => line.Y0 is > 0.05f and < 0.25f && MathF.Abs(line.X0) < 0.2f);
    }

    [Fact]
    public void HiddenMarkers_OmitTheDrawings()
    {
        var frame = HudLayout.Build(0, 0, 0, clock: null, new HudLayoutOptions { ShowMarkers = false });

        Assert.DoesNotContain(frame.Lines, line => line.Part == HudPart.Marker);
        Assert.Contains(frame.Lines, line => line.Part == HudPart.Reticle);
    }

    [Fact]
    public void Clock_AppearsOnlyWhenEnabled()
    {
        var shown = HudLayout.Build(0, 0, 0, "21:05");
        var hidden = HudLayout.Build(0, 0, 0, "21:05", new HudLayoutOptions { ShowClock = false });

        Assert.Contains(shown.Labels, label => label.Text == "21:05");
        Assert.DoesNotContain(hidden.Labels, label => label.Text == "21:05");
    }

    private static bool Near(float value, float expected) => MathF.Abs(value - expected) < 0.001f;
}
