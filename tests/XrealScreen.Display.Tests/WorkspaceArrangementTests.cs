using XrealScreen.Core.Abstractions;
using XrealScreen.Core.Workspace;

namespace XrealScreen.Display.Tests;

public class WorkspaceArrangementTests
{
    private static DisplayMonitor M(string gdi, string path, int x, int w, bool primary = false) =>
        new(gdi, gdi, "X", 1, path, new Resolution(w, 1080), x, 0, 60, primary, "HDMI", 1, 1);

    [Fact]
    public void VirtualMonitorsGoNextToDesk_GlassesLast()
    {
        var monitors = new[]
        {
            M("D1", "desk", 0, 1920, primary: true),
            M("G", "glasses", 1920, 1920),
            M("V1", "v1", 3840, 1920),
            M("V2", "v2", 5760, 3840),
        };

        var layout = WorkspaceArrangement.Arrange(monitors, ["V1", "V2"], "G");
        int X(string path) => layout.Monitors.Single(m => m.DevicePath == path).X;

        Assert.Equal(0, X("desk"));
        Assert.Equal(1920, X("v1"));
        Assert.Equal(3840, X("v2"));
        Assert.Equal(7680, X("glasses"));
    }

    [Fact]
    public void GlassesPrimary_VirtualMonitorsGoLeftOfDesk_NoOverlap()
    {
        // Windows made the glasses primary (21:9): desk at -1920, glasses at 0.
        var monitors = new[]
        {
            M("D1", "desk", -1920, 1920),
            M("G", "glasses", 0, 2560, primary: true),
            M("V1", "v1", 2560, 1270),
            M("V2", "v2", 3830, 1270),
        };

        var layout = WorkspaceArrangement.Arrange(monitors, ["V1", "V2"], "G");
        int X(string path) => layout.Monitors.Single(m => m.DevicePath == path).X;

        Assert.Equal(0, X("glasses"));
        Assert.Equal(-1920, X("desk"));
        Assert.Equal(-1920 - 2540, X("v1"));
        Assert.Equal(-1920 - 1270, X("v2"));
        var spans = layout.Monitors.Select(m => (m.X, End: m.X + m.Width)).OrderBy(s => s.X).ToList();
        for (int i = 1; i < spans.Count; i++)
        {
            Assert.True(spans[i].X >= spans[i - 1].End, "no overlapping monitors");
        }
    }

    [Fact]
    public void DeskOnTheLeftOfPrimary_IsKept()
    {
        var monitors = new[]
        {
            M("D2", "left-desk", -1920, 1920),
            M("D1", "desk", 0, 1920, primary: true),
            M("G", "glasses", 1920, 1920),
            M("V1", "v1", 3840, 1920),
        };

        var layout = WorkspaceArrangement.Arrange(monitors, ["V1"], "G");
        Assert.Equal(-1920, layout.Monitors.Single(m => m.DevicePath == "left-desk").X);
        Assert.Equal(1920, layout.Monitors.Single(m => m.DevicePath == "v1").X);
        Assert.Equal(3840, layout.Monitors.Single(m => m.DevicePath == "glasses").X);
    }

    [Fact]
    public void PrimaryIndex_ShiftsLayoutSoThatVirtualScreenIsAtOrigin()
    {
        var monitors = new[]
        {
            M("D1", "desk", 0, 1920, primary: true),
            M("G", "glasses", 1920, 1920),
            M("V1", "v1", 3840, 1920),
            M("V2", "v2", 5760, 1920),
        };

        var layout = WorkspaceArrangement.Arrange(monitors, ["V1", "V2"], "G", primaryIndex: 1);
        MonitorPlacement P(string path) => layout.Monitors.Single(m => m.DevicePath == path);

        Assert.Equal(0, P("v2").X);
        Assert.True(P("v2").IsPrimary);
        Assert.Single(layout.Monitors, m => m.IsPrimary);
        Assert.Equal(-3840, P("desk").X); // same order: [desk][v1][v2][glasses]
        Assert.Equal(-1920, P("v1").X);
        Assert.Equal(1920, P("glasses").X);
    }

    [Fact]
    public void Grid_SixScreens_TwoRowsOfThree_GlassesToTheRight()
    {
        var monitors = new List<DisplayMonitor> { M("D1", "desk", 0, 1920, primary: true), M("G", "glasses", 1920, 1920) };
        for (int i = 1; i <= 6; i++)
        {
            monitors.Add(M($"V{i}", $"v{i}", 4000 + i * 1920, 1920));
        }

        var layout = WorkspaceArrangement.Arrange(monitors, ["V1", "V2", "V3", "V4", "V5", "V6"], "G", gridColumns: 3);
        MonitorPlacement P(string path) => layout.Monitors.Single(m => m.DevicePath == path);

        Assert.Equal(0, P("desk").X);
        Assert.Equal((1920, 0), (P("v1").X, P("v1").Y));
        Assert.Equal((3840, 0), (P("v2").X, P("v2").Y));
        Assert.Equal((5760, 0), (P("v3").X, P("v3").Y));
        Assert.Equal((1920, 1080), (P("v4").X, P("v4").Y));
        Assert.Equal((3840, 1080), (P("v5").X, P("v5").Y));
        Assert.Equal((5760, 1080), (P("v6").X, P("v6").Y));
        Assert.Equal((7680, 0), (P("glasses").X, P("glasses").Y));
        AssertNoOverlap(layout);
    }

    [Fact]
    public void Grid_FiveScreens_ShortRowIsCentered()
    {
        var monitors = new List<DisplayMonitor> { M("D1", "desk", 0, 1920, primary: true), M("G", "glasses", 1920, 1920) };
        for (int i = 1; i <= 5; i++)
        {
            monitors.Add(M($"V{i}", $"v{i}", 8000, 1920));
        }

        var layout = WorkspaceArrangement.Arrange(monitors, ["V1", "V2", "V3", "V4", "V5"], "G", gridColumns: 3);
        MonitorPlacement P(string path) => layout.Monitors.Single(m => m.DevicePath == path);

        Assert.Equal(1920, P("v1").X);
        Assert.Equal(2880, P("v4").X); // centered under the top row
        Assert.Equal(4800, P("v5").X);
        Assert.Equal(1080, P("v4").Y);
        Assert.Equal(7680, P("glasses").X);
        AssertNoOverlap(layout);
    }

    [Fact]
    public void Grid_SixScreens_GlassesPrimary_SitsLeftOfTheDesk()
    {
        var monitors = new List<DisplayMonitor>
        {
            M("D1", "desk", -1920, 1920),
            M("G", "glasses", 0, 1920, primary: true),
        };
        for (int i = 1; i <= 6; i++)
        {
            monitors.Add(M($"V{i}", $"v{i}", 2000, 1920));
        }

        var layout = WorkspaceArrangement.Arrange(monitors, ["V1", "V2", "V3", "V4", "V5", "V6"], "G", gridColumns: 3);
        MonitorPlacement P(string path) => layout.Monitors.Single(m => m.DevicePath == path);

        Assert.Equal(0, P("glasses").X);
        Assert.Equal(-1920, P("desk").X);
        Assert.Equal(-7680, P("v1").X);
        Assert.Equal(-7680, P("v4").X);
        Assert.Equal(1080, P("v4").Y);
        Assert.Equal(-3840, P("v3").X);
        AssertNoOverlap(layout);
    }

    [Fact]
    public void Arc_SixScreens_StaysOneRow()
    {
        var monitors = new List<DisplayMonitor> { M("D1", "desk", 0, 1920, primary: true), M("G", "glasses", 1920, 1920) };
        for (int i = 1; i <= 6; i++)
        {
            monitors.Add(M($"V{i}", $"v{i}", 9000, 1920));
        }

        var layout = WorkspaceArrangement.Arrange(monitors, ["V1", "V2", "V3", "V4", "V5", "V6"], "G");
        Assert.All(layout.Monitors, m => Assert.Equal(0, m.Y));
        Assert.Equal(1920 + 6 * 1920, layout.Monitors.Single(m => m.DevicePath == "glasses").X);
    }

    private static void AssertNoOverlap(DisplayLayout layout)
    {
        var boxes = layout.Monitors.Select(m => (m.X, m.Y, Right: m.X + m.Width, Bottom: m.Y + m.Height)).ToList();
        for (int i = 0; i < boxes.Count; i++)
        {
            for (int j = i + 1; j < boxes.Count; j++)
            {
                bool overlaps = boxes[i].X < boxes[j].Right && boxes[j].X < boxes[i].Right
                    && boxes[i].Y < boxes[j].Bottom && boxes[j].Y < boxes[i].Bottom;
                Assert.False(overlaps, $"monitors {i} and {j} overlap");
            }
        }
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(5)]
    public void PrimaryIndex_OutOfRange_KeepsPrimary(int index)
    {
        var monitors = new[] { M("D1", "desk", 0, 1920, primary: true), M("G", "glasses", 1920, 1920), M("V1", "v1", 3840, 1920) };
        var layout = WorkspaceArrangement.Arrange(monitors, ["V1"], "G", index);
        Assert.True(layout.Monitors.Single(m => m.DevicePath == "desk").IsPrimary);
    }

    [Fact]
    public void AppOpen_WindowOnTheDesk_MovesOntoTheVirtualScreen()
    {
        var screen = new System.Drawing.Rectangle(3840, 0, 1920, 1080);
        var window = new System.Drawing.Rectangle(200, 100, 800, 600);

        Assert.Equal(screen, AppOpenPlacement.ScreenAt(new System.Drawing.Point(4000, 40), [screen]));
        Assert.Null(AppOpenPlacement.ScreenAt(new System.Drawing.Point(10, 10), [screen]));
        Assert.Equal(new System.Drawing.Rectangle(3840 + 560, 240, 800, 600), AppOpenPlacement.MoveTarget(window, screen));
    }

    [Fact]
    public void AppOpen_WindowAlreadyOnTheScreen_Stays()
    {
        var screen = new System.Drawing.Rectangle(3840, 0, 1920, 1080);
        var window = new System.Drawing.Rectangle(4000, 80, 1400, 900);
        Assert.Null(AppOpenPlacement.MoveTarget(window, screen));
    }

    [Fact]
    public void AppOpen_WiderThanTheScreen_ShrinksToTheScreen()
    {
        var screen = new System.Drawing.Rectangle(0, 0, 1600, 1000);
        var window = new System.Drawing.Rectangle(100, 100, 3840, 2160);
        Assert.Equal(new System.Drawing.Rectangle(0, 0, 1600, 1000), AppOpenPlacement.MoveTarget(window, screen));
    }

    [Theory]
    [InlineData(100, 50, 400, 300, 3940, 50, 400, 300)] // same offset on the target
    [InlineData(1800, 900, 400, 300, 5360, 780, 400, 300)] // pushed back inside
    [InlineData(-50, 0, 2500, 1200, 3840, 0, 1920, 1080)] // shrunk to fit
    public void WindowMover_Relocate_KeepsOffsetAndFits(int x, int y, int w, int h, int ex, int ey, int ew, int eh)
    {
        var from = new System.Drawing.Rectangle(0, 0, 1920, 1080);
        var to = new System.Drawing.Rectangle(3840, 0, 1920, 1080);
        Assert.Equal(new System.Drawing.Rectangle(ex, ey, ew, eh), WindowMover.Relocate(new System.Drawing.Rectangle(x, y, w, h), from, to));
    }
}
