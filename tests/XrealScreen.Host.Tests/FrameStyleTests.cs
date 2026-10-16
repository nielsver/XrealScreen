using XrealScreen.Host;

namespace XrealScreen.Host.Tests;

public class FrameStyleTests
{
    [Fact]
    public void MissingStyle_KeepsTheOldGradientSwitch()
    {
        Assert.Equal(FrameStyle.Linear, FrameStyles.Resolve(null, null));
        Assert.Equal(FrameStyle.Linear, FrameStyles.Resolve(null, true));
        Assert.Equal(FrameStyle.SeeThrough, FrameStyles.Resolve(null, false));
    }

    [Fact]
    public void ExplicitStyle_Wins()
    {
        Assert.Equal(FrameStyle.Vignette, FrameStyles.Resolve(FrameStyle.Vignette, false));
        Assert.Equal(3, FrameStyles.ShaderIndex(FrameStyle.Vignette));
        Assert.Equal(4, FrameStyles.ShaderIndex(FrameStyle.Glow));
    }
}
