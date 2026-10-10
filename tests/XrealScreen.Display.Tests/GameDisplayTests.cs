using System.Drawing;
using XrealScreen.Core.Abstractions;
using XrealScreen.Core.Workspace;

namespace XrealScreen.Display.Tests;

public class GameDisplayTests
{
    private static DisplayMonitor M(string gdi, int x, int y, int w, int h, bool primary = false) =>
        new(gdi, gdi, "X", 1, gdi, new Resolution(w, h), x, y, 60, primary, "HDMI", 1, 1);

    [Fact]
    public void GlassesPrimary_ShiftsLayoutSoGlassesAreAtOrigin()
    {
        var monitors = new[] { M("desk", 0, 0, 2560, 1440, primary: true), M("glasses", 2560, 200, 1920, 1080) };

        var layout = GameDisplay.GlassesPrimary(monitors, "glasses");
        var desk = layout.Monitors.Single(m => m.DevicePath == "desk");
        var glasses = layout.Monitors.Single(m => m.DevicePath == "glasses");

        Assert.Equal((0, 0, true), (glasses.X, glasses.Y, glasses.IsPrimary));
        Assert.Equal((-2560, -200, false), (desk.X, desk.Y, desk.IsPrimary));
    }

    [Fact]
    public void FitOnto_BorderlessFullscreenWindow_FillsTarget()
    {
        var desk = new Rectangle(0, 0, 2560, 1440);
        var glasses = new Rectangle(2560, 0, 1920, 1080);

        Assert.Equal(glasses, GameDisplay.FitOnto(desk, desk, glasses));
    }

    [Fact]
    public void FitOnto_SmallerWindow_IsCenteredAndShrunk()
    {
        var desk = new Rectangle(0, 0, 2560, 1440);
        var glasses = new Rectangle(2560, 0, 1920, 1080);

        Assert.Equal(new Rectangle(2560 + 320, 0 + 90, 1280, 900), GameDisplay.FitOnto(new Rectangle(100, 100, 1280, 900), desk, glasses));
        Assert.Equal(new Rectangle(2560, 0, 1920, 1080), GameDisplay.FitOnto(new Rectangle(10, 10, 2400, 1300), desk, glasses));
    }
}
