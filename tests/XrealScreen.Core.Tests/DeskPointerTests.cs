using XrealScreen.Core.Hud;

namespace XrealScreen.Core.Tests;

public class DeskPointerTests
{
    [Fact]
    public void Map_MatchingAspect_CenterLandsOnCenter()
    {
        var glasses = new DeskPointer.Rect(1000, 200, 200, 100);
        var desk = new DeskPointer.Rect(10, 20, 400, 200);

        DeskPointer.Point? point = DeskPointer.Map(glasses, desk, 1100, 250);

        Assert.Equal(new DeskPointer.Point(210, 120), point);
    }

    [Fact]
    public void Map_WiderFrame_CenterLandsOnCenter()
    {
        var glasses = new DeskPointer.Rect(0, 0, 320, 100);
        var desk = new DeskPointer.Rect(500, 40, 160, 100);

        DeskPointer.Point? point = DeskPointer.Map(glasses, desk, 160, 50);

        Assert.Equal(new DeskPointer.Point(580, 90), point);
    }

    [Fact]
    public void Map_LeftBar_ClampsToTheDeskEdge()
    {
        var glasses = new DeskPointer.Rect(0, 0, 320, 100);
        var desk = new DeskPointer.Rect(500, 40, 160, 100);

        DeskPointer.Point? point = DeskPointer.Map(glasses, desk, 0, 50);

        Assert.Equal(500, point?.X);
        Assert.Equal(90, point?.Y);
    }

    [Fact]
    public void Map_RightBar_StaysInsideTheDesk()
    {
        var glasses = new DeskPointer.Rect(0, 0, 320, 100);
        var desk = new DeskPointer.Rect(500, 40, 160, 100);

        DeskPointer.Point? point = DeskPointer.Map(glasses, desk, 300, 50);

        Assert.Equal(659, point?.X);
        Assert.Equal(90, point?.Y);
    }

    [Fact]
    public void Map_RejectsAnEmptyScreen()
    {
        var glasses = new DeskPointer.Rect(0, 0, 100, 100);
        var desk = new DeskPointer.Rect(0, 0, 0, 100);

        Assert.Null(DeskPointer.Map(glasses, desk, 50, 50));
    }

    [Fact]
    public void Redirect_SkipsAPointThatIsOnBothScreens()
    {
        var area = new DeskPointer.Rect(0, 0, 100, 100);

        Assert.False(DeskPointer.Redirect(area, area, 10, 10));
    }

    [Fact]
    public void Redirect_TakesAPointOnTheGlassesOnly()
    {
        var glasses = new DeskPointer.Rect(1000, 0, 100, 100);
        var desk = new DeskPointer.Rect(0, 0, 100, 100);

        Assert.True(DeskPointer.Redirect(glasses, desk, 1050, 50));
        Assert.False(DeskPointer.Redirect(glasses, desk, 50, 50));
    }
}
