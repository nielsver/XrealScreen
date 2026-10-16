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

    private static readonly Resolution HeadPicture = new(1920, 1200);

    [Fact]
    public void Fit_OneScreen_FillsTheGlassesPicture()
    {
        var fitted = WorkspaceLayout.FitView(HeadPicture, 1, LayoutPreset.Arc, 1, 1, 0, 0, 1.5f);

        Assert.Equal(new PixelRect(0, 0, 1920, 1200), fitted[0].Pixels);
        Assert.Equal(0f, fitted[0].Placement.YawDegrees, 2);
        Assert.Equal(0f, fitted[0].Placement.PitchDegrees, 2);
        Assert.Equal(22.5f, EdgeYaw(fitted[0].Placement, 1), 2);
        Assert.Equal(14.5f, EdgePitch(fitted[0].Placement, 1), 2);
    }

    [Fact]
    public void Fit_SixGrid_UsesTheFullPictureAndStaysInFront()
    {
        var fitted = WorkspaceLayout.FitView(HeadPicture, 6, LayoutPreset.Grid, 1, 1, 0, 0, 3f);

        Assert.Equal(6, fitted.Count);
        Assert.Equal(new PixelRect(0, 0, 640, 600), fitted[0].Pixels);
        Assert.Equal(new PixelRect(640, 0, 640, 600), fitted[1].Pixels);
        Assert.Equal(new PixelRect(1280, 0, 640, 600), fitted[2].Pixels);
        Assert.Equal(new PixelRect(0, 600, 640, 600), fitted[3].Pixels);
        Assert.Equal(new PixelRect(1280, 600, 640, 600), fitted[5].Pixels);
        Assert.All(fitted.Take(3), s => Assert.True(s.Placement.PitchDegrees > 0));
        Assert.All(fitted.Skip(3), s => Assert.True(s.Placement.PitchDegrees < 0));
        Assert.Equal(22.5f, EdgeYaw(fitted[0].Placement, 1), 2);
        Assert.Equal(-22.5f, EdgeYaw(fitted[2].Placement, -1), 2);
        Assert.Equal(14.5f, EdgePitch(fitted[0].Placement, 1), 2);
        Assert.Equal(-14.5f, EdgePitch(fitted[5].Placement, -1), 2);
        Assert.All(fitted, s => Assert.True(MathF.Abs(s.Placement.YawDegrees) < 23f));
    }

    [Fact]
    public void Fit_SixArc_StaysOneRowInsideThePicture()
    {
        var fitted = WorkspaceLayout.FitView(HeadPicture, 6, LayoutPreset.Arc, 1, 1, 0, 0, 1.5f);

        Assert.Equal(6, fitted.Count);
        Assert.All(fitted, s => Assert.Equal(320, s.Pixels.Width));
        Assert.All(fitted, s => Assert.Equal(1200, s.Pixels.Height));
        Assert.All(fitted, s => Assert.Equal(0f, s.Placement.PitchDegrees, 2));
        Assert.Equal(22.5f, EdgeYaw(fitted[0].Placement, 1), 2);
        Assert.Equal(-22.5f, EdgeYaw(fitted[5].Placement, -1), 2);
        Assert.All(fitted, s => Assert.True(MathF.Abs(s.Placement.YawDegrees) < 23f));
    }

    [Fact]
    public void Fit_HalfWidth_LeavesSpaceOnBothSides()
    {
        var fitted = WorkspaceLayout.FitView(HeadPicture, 1, LayoutPreset.Arc, 0.5, 1, 0, 0, 1.5f);

        Assert.Equal(new PixelRect(480, 0, 960, 1200), fitted[0].Pixels);
        Assert.Equal(0f, fitted[0].Placement.YawDegrees, 2);
        Assert.True(EdgeYaw(fitted[0].Placement, 1) < 20f);
    }

    [Fact]
    public void Fit_DistanceChangesSizeInMeters_NotTheAngle()
    {
        var near = WorkspaceLayout.FitView(HeadPicture, 1, LayoutPreset.Arc, 1, 1, 0, 0, 1.5f)[0].Placement;
        var far = WorkspaceLayout.FitView(HeadPicture, 1, LayoutPreset.Arc, 1, 1, 0, 0, 3f)[0].Placement;

        Assert.Equal(near.YawDegrees, far.YawDegrees, 2);
        Assert.Equal(near.WidthMeters * 2f, far.WidthMeters, 2);
    }

    [Fact]
    public void TooManyScreens_Throws()
    {
        var screens = Enumerable.Range(1, WorkspaceLayout.MaxScreens + 1).Select(i => new ScreenSpec(i, UltrawideMode.Off)).ToList();
        Assert.Throws<ArgumentOutOfRangeException>(() => WorkspaceLayout.Compute(screens, new WorkspaceSettings()));
    }

    private static float EdgeYaw(ScreenPlacement placement, float side) =>
        placement.YawDegrees + side * placement.WidthMeters / placement.DistanceMeters * (180f / MathF.PI) / 2f;

    private static float EdgePitch(ScreenPlacement placement, float side)
    {
        float yaw = placement.YawDegrees * MathF.PI / 180f;
        float radius = placement.DistanceMeters;
        float centerZ = MathF.Tan(placement.PitchDegrees * MathF.PI / 180f) * radius;
        float z = centerZ + side * placement.HeightMeters / 2f;
        return MathF.Atan2(z, radius * MathF.Cos(yaw)) * 180f / MathF.PI;
    }
}
