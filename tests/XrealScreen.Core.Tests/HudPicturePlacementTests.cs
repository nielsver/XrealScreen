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
}
