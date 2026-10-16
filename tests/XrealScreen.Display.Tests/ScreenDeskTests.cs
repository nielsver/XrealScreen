using System.Drawing;
using XrealScreen.Display;

namespace XrealScreen.Display.Tests;

public class ScreenDeskTests
{
    [Theory]
    [InlineData(0, 3, 0)]
    [InlineData(1, 3, 0)]
    [InlineData(2, 3, 1)]
    [InlineData(3, 3, 2)]
    [InlineData(4, 2, 0)]
    public void CursorScreen_UsesThePrimaryVirtualScreenOrTheFirst(int primary, int count, int expected) =>
        Assert.Equal(expected, ScreenDesk.CursorScreen(primary, count));

    [Fact]
    public void Center_IsTheMiddleOfTheMonitor() =>
        Assert.Equal(new Point(4800, 540), ScreenDesk.Center(new Rectangle(3840, 0, 1920, 1080)));
}
