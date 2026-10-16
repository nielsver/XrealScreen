using XrealScreen.Core.Workspace;

namespace XrealScreen.Core.Tests;

public class AnchorSplitTests
{
    private static readonly Resolution Wide32x9 = new(3840, 1080);

    [Theory]
    [InlineData(3840, 1080, true)]   // 32:9
    [InlineData(2560, 1080, true)]   // 21:9
    [InlineData(1920, 2160, true)]   // 16:18
    [InlineData(1920, 1200, false)]  // UltraWide off
    [InlineData(1920, 1080, false)]
    public void DetectsUltrawideSignals(int w, int h, bool expected) =>
        Assert.Equal(expected, AnchorSplit.IsUltrawideSignal(new Resolution(w, h)));

    [Fact]
    public void Wide32x9_TwoScreens_Fill_Are1080p()
    {
        var r = AnchorSplit.Split(Wide32x9, 2);
        Assert.Equal([new PixelRect(0, 0, 1920, 1080), new PixelRect(1920, 0, 1920, 1080)], r);
    }

    [Fact]
    public void Wide32x9_TwoScreens_Gap_16x9_AreCenteredAndKeepAspect()
    {
        var r = AnchorSplit.Split(Wide32x9, 2, gapPixels: 32, AnchorAspect.Ratio16x9);
        Assert.Equal(1904, r[0].Width);
        Assert.Equal(1070, r[0].Height);                    // 1904 * 9/16 = 1071 → even
        Assert.Equal(32, r[1].X - (r[0].X + r[0].Width));   // exact gap
        Assert.Equal(r[0].X, Wide32x9.Width - (r[1].X + r[1].Width)); // centered horizontally
        Assert.Equal(5, r[0].Y);                             // centered vertically: (1080-1070)/2
    }

    [Fact]
    public void Wide32x9_ThreeScreens_16x9_AreLimitedByWidth()
    {
        var r = AnchorSplit.Split(Wide32x9, 3, gapPixels: 40, AnchorAspect.Ratio16x9);
        Assert.All(r, s => Assert.InRange(s.Width / (double)s.Height, 1.77, 1.785));
        Assert.True(r[2].X + r[2].Width <= Wide32x9.Width);
        Assert.Equal(40, r[1].X - (r[0].X + r[0].Width));
    }

    [Fact]
    public void Tall16x18_IsStacked_WithGap()
    {
        var r = AnchorSplit.Split(new Resolution(1920, 2160), 2, gapPixels: 40, AnchorAspect.Ratio16x9);
        Assert.Equal(r[0].X, r[1].X);
        Assert.Equal(40, r[1].Y - (r[0].Y + r[0].Height));
        Assert.InRange(r[0].Width / (double)r[0].Height, 1.77, 1.785);
        Assert.True(r[1].Y + r[1].Height <= 2160);
    }

    [Fact]
    public void Fill_UsesWholeSlot()
    {
        var r = AnchorSplit.Split(Wide32x9, 3, gapPixels: 0, AnchorAspect.Fill);
        Assert.All(r, s => Assert.Equal(1280, s.Width));
        Assert.All(r, s => Assert.Equal(1080, s.Height));
    }

    [Fact]
    public void HalfWidth_PullsOneScreenInFromTheSides()
    {
        var full = AnchorSplit.Place(Wide32x9, 1, gapPixels: 0, AnchorAspect.Fill, framePx: 0);
        var half = AnchorSplit.Place(Wide32x9, 1, gapPixels: 0, AnchorAspect.Fill, framePx: 0, widthScale: 0.5);

        Assert.Equal(3840, full[0].Width);
        Assert.Equal(1920, half[0].Width);
        Assert.Equal(full[0].Height, half[0].Height);
        Assert.Equal(960, half[0].X);
        Assert.Equal(full[0].X + full[0].Width / 2, half[0].X + half[0].Width / 2);
    }

    [Fact]
    public void HalfWidth_ThreeScreens_StaySideBySideAndNarrower()
    {
        var full = AnchorSplit.Place(Wide32x9, 3, gapPixels: 0, AnchorAspect.Fill, framePx: 0);
        var half = AnchorSplit.ScaleWidth(full, 0.5);

        Assert.All(half, s => Assert.Equal(640, s.Width));
        Assert.Equal(full[0].Height, half[0].Height);
        Assert.True(half[0].X > full[0].X);
        Assert.True(half[2].X + half[2].Width < full[2].X + full[2].Width);
        Assert.True(half[1].X > half[0].X + half[0].Width - 2);
    }

    [Fact]
    public void WithoutColumns_SixScreens_StaysOneRowOfThree()
    {
        var r = AnchorSplit.Split(Wide32x9, 6);
        Assert.Equal(3, r.Count);
        Assert.All(r, s => Assert.Equal(0, s.Y));
    }

    [Fact]
    public void Grid_SixScreens_TwoRowsOfThree_FillsTheWideImage()
    {
        var r = AnchorSplit.Split(Wide32x9, 6, gapPixels: 0, columns: 3);
        Assert.Equal(6, r.Count);
        Assert.Equal([0, 1280, 2560], r.Take(3).Select(s => s.X).ToArray());
        Assert.Equal([0, 1280, 2560], r.Skip(3).Select(s => s.X).ToArray());
        Assert.All(r.Take(3), s => Assert.Equal(0, s.Y));
        Assert.All(r.Skip(3), s => Assert.Equal(540, s.Y));
        Assert.All(r, s => Assert.Equal(new PixelRect(s.X, s.Y, 1280, 540), s));
    }

    [Fact]
    public void Grid_FourScreens_BottomScreenIsCentered()
    {
        var r = AnchorSplit.Split(Wide32x9, 4, gapPixels: 0, columns: 3);
        Assert.Equal(4, r.Count);
        Assert.Equal(1280, r[3].X);
        Assert.Equal(540, r[3].Y);
        Assert.Equal(1280, r[3].Width);
    }

    [Fact]
    public void Grid_ThreeScreens_StaysOneRow()
    {
        var row = AnchorSplit.Split(Wide32x9, 3, gapPixels: 0);
        var grid = AnchorSplit.Split(Wide32x9, 3, gapPixels: 0, columns: 3);
        Assert.Equal(row, grid);
    }

    [Fact]
    public void Grid_Place_KeepsTheFrameClear()
    {
        var r = AnchorSplit.Place(Wide32x9, 6, gapPixels: 16, AnchorAspect.Fill, framePx: 36, columns: 3);
        Assert.Equal(6, r.Count);
        Assert.All(r, s => Assert.True(s.X >= 36 && s.Y >= 36, s.ToString()));
        Assert.All(r, s => Assert.True(s.X + s.Width <= 3840 - 36 && s.Y + s.Height <= 1080 - 36, s.ToString()));
        Assert.True(r[3].Y >= r[0].Y + r[0].Height);
        Assert.Equal(r[0].X, r[3].X);
    }

    [Fact]
    public void Place_LeavesABorderAroundTheScreens()
    {
        var r = AnchorSplit.Place(Wide32x9, 3, gapPixels: 16, AnchorAspect.Fill, framePx: 36);
        Assert.Equal(3, r.Count);
        Assert.True(r[0].X >= 36);
        Assert.True(r[0].Y >= 36);
        Assert.True(r[2].X + r[2].Width <= 3840 - 36);
        Assert.True(r[0].Y + r[0].Height <= 1080 - 36);
    }
}
