using XrealScreen.Core.Workspace;

namespace XrealScreen.Core.Tests;

public class WorkspaceTests
{
    [Theory]
    [InlineData(UltrawideMode.Off, 1920, 1080)]
    [InlineData(UltrawideMode.Wide21x9, 2560, 1080)]
    [InlineData(UltrawideMode.Wide32x9, 3840, 1080)]
    [InlineData(UltrawideMode.Tall16x18, 1920, 2160)]
    public void UltrawideResolutions(UltrawideMode mode, int width, int height) =>
        Assert.Equal(new Resolution(width, height), UltrawideModes.GetResolution(mode));

    [Fact]
    public void Tall16x18_IsTwo16x9Stacked()
    {
        var r = UltrawideModes.GetResolution(UltrawideMode.Tall16x18);
        Assert.Equal(16.0 / 18.0, r.AspectRatio, 6);
    }

    [Fact]
    public void Arc_ThreeScreens_IsSymmetricAroundCenter()
    {
        var screens = Enumerable.Range(1, 3).Select(i => new ScreenSpec(i, UltrawideMode.Off)).ToList();
        var p = WorkspaceLayout.Compute(screens, new WorkspaceSettings());

        Assert.Equal(3, p.Count);
        Assert.Equal(0f, p[1].YawDegrees, 3);
        Assert.Equal(-p[0].YawDegrees, p[2].YawDegrees, 3);
        Assert.True(p[0].YawDegrees > 0, "first screen is on the left (positive yaw)");
        Assert.All(p, s => Assert.Equal(0f, s.PitchDegrees, 3));
    }

    [Fact]
    public void Grid_FourScreens_TwoRows()
    {
        var screens = Enumerable.Range(1, 4).Select(i => new ScreenSpec(i, UltrawideMode.Off)).ToList();
        var p = WorkspaceLayout.Compute(screens, new WorkspaceSettings { Preset = LayoutPreset.Grid });

        Assert.True(p[0].PitchDegrees > 0);
        Assert.True(p[3].PitchDegrees < 0);
        Assert.Equal(p[0].PitchDegrees, -p[3].PitchDegrees, 3);
        Assert.Equal(0f, p[3].YawDegrees, 3); // the single bottom screen is centered
    }

    [Fact]
    public void Grid_SixScreens_TwoRowsOfThree_StaysInFront()
    {
        var screens = Enumerable.Range(1, 6).Select(i => new ScreenSpec(i, UltrawideMode.Off)).ToList();
        var grid = WorkspaceLayout.Compute(screens, new WorkspaceSettings { Preset = LayoutPreset.Grid });
        var arc = WorkspaceLayout.Compute(screens, new WorkspaceSettings { Preset = LayoutPreset.Arc });

        Assert.Equal(6, grid.Count);
        Assert.All(grid.Take(3), s => Assert.True(s.PitchDegrees > 0));
        Assert.All(grid.Skip(3), s => Assert.True(s.PitchDegrees < 0));
        Assert.Equal(grid[0].PitchDegrees, grid[1].PitchDegrees, 3);
        Assert.Equal(grid[0].PitchDegrees, -grid[3].PitchDegrees, 3);
        Assert.Equal(0f, grid[1].YawDegrees, 3);
        Assert.Equal(0f, grid[4].YawDegrees, 3);
        Assert.Equal(-grid[0].YawDegrees, grid[2].YawDegrees, 3);
        Assert.Equal(-grid[3].YawDegrees, grid[5].YawDegrees, 3);
        Assert.True(grid[0].YawDegrees > 0, "first screen is on the left");

        float gridYaw = grid.Max(s => MathF.Abs(s.YawDegrees));
        float arcYaw = arc.Max(s => MathF.Abs(s.YawDegrees));
        Assert.True(gridYaw < 60f, $"grid outer yaw {gridYaw:F1}°");
        Assert.True(arcYaw > 100f, $"arc outer yaw {arcYaw:F1}°");
    }

    [Theory]
    [InlineData(3, 4, LayoutPreset.Arc, LayoutPreset.Grid)]
    [InlineData(3, 6, LayoutPreset.Arc, LayoutPreset.Grid)]
    [InlineData(1, 3, LayoutPreset.Arc, LayoutPreset.Arc)]
    [InlineData(4, 6, LayoutPreset.Arc, LayoutPreset.Arc)]
    [InlineData(3, 6, LayoutPreset.Grid, LayoutPreset.Grid)]
    [InlineData(6, 3, LayoutPreset.Grid, LayoutPreset.Grid)]
    public void PresetForCount_SelectsGridOnlyWhenCrossingPastThree(int previous, int count, LayoutPreset current, LayoutPreset expected) =>
        Assert.Equal(expected, WorkspaceLayout.PresetForCount(previous, count, current));

    [Fact]
    public void TooManyScreens_Throws()
    {
        var screens = Enumerable.Range(1, WorkspaceLayout.MaxScreens + 1).Select(i => new ScreenSpec(i, UltrawideMode.Off)).ToList();
        Assert.Throws<ArgumentOutOfRangeException>(() => WorkspaceLayout.Compute(screens, new WorkspaceSettings()));
    }
}
