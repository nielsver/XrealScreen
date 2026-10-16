using XrealScreen.Core.Abstractions;
using XrealScreen.Core.Workspace;

namespace XrealScreen.Core.Tests;

public class GlassesPictureSetupTests
{
    private static DisplayMode Mode(int width, int height, int hz, int bpp = 32) =>
        new(new Resolution(width, height), hz, bpp);

    [Fact]
    public void Anchor_Prefers3840x1080At60()
    {
        DisplayMode? chosen = GlassesPictureSetup.Choose(true,
        [
            Mode(1920, 1080, 120),
            Mode(3840, 1080, 90),
            Mode(3840, 1080, 60),
            Mode(2560, 1080, 60),
        ]);

        Assert.Equal(Mode(3840, 1080, 60), chosen);
    }

    [Fact]
    public void Anchor_Prefers32BitsWhenTheRateMatches()
    {
        DisplayMode? chosen = GlassesPictureSetup.Choose(true, [Mode(3840, 1080, 60, 16), Mode(3840, 1080, 60, 32)]);
        Assert.Equal(32, chosen?.BitsPerPixel);
    }

    [Fact]
    public void Tracking_Prefers1920x1080At120_AndSkipsUltrawide()
    {
        DisplayMode? chosen = GlassesPictureSetup.Choose(false,
        [
            Mode(3840, 1080, 60),
            Mode(1920, 1200, 60),
            Mode(1920, 1080, 90),
            Mode(1920, 1080, 120),
        ]);

        Assert.Equal(Mode(1920, 1080, 120), chosen);
    }

    [Fact]
    public void EmptyList_ReturnsNull()
    {
        Assert.Null(GlassesPictureSetup.Choose(true, []));
        Assert.Null(GlassesPictureSetup.Choose(false, []));
    }
}
