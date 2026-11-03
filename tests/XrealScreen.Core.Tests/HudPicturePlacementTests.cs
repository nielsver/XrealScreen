using XrealScreen.Core.Hud;

namespace XrealScreen.Core.Tests;

public class HudPicturePlacementTests
{
    [Fact]
    public void Contain_FillsAMatchingFrame()
    {
        (float left, float right, float top, float bottom) = HudPicturePlacement.Contain(160, 100, 160, 100);

        Assert.Equal(-1f, left, 3);
        Assert.Equal(1f, right, 3);
        Assert.Equal(1f, top, 3);
        Assert.Equal(-1f, bottom, 3);
    }

    [Fact]
    public void Contain_LeavesTheSidesEmptyOnAWiderFrame()
    {
        (float left, float right, float top, float bottom) = HudPicturePlacement.Contain(100, 100, 160, 100);

        Assert.Equal(-0.625f, left, 3);
        Assert.Equal(0.625f, right, 3);
        Assert.Equal(1f, top, 3);
        Assert.Equal(-1f, bottom, 3);
    }

    [Fact]
    public void Place_PutsTheCenterWhereAsked()
    {
        (float left, float right, float top, float bottom) = HudPicturePlacement.Place(20, 20, 160, 100, 0.5f, 0f, 0.4f);

        Assert.Equal(0.375f, left, 3);
        Assert.Equal(0.625f, right, 3);
        Assert.Equal(0.2f, top, 3);
        Assert.Equal(-0.2f, bottom, 3);
    }

    [Fact]
    public void Place_RejectsAnEmptyPicture()
    {
        (float left, float right, float top, float bottom) = HudPicturePlacement.Place(0, 20, 160, 100, 0f, 0f, 0.4f);

        Assert.Equal(0f, left);
        Assert.Equal(0f, right);
        Assert.Equal(0f, top);
        Assert.Equal(0f, bottom);
    }
}
