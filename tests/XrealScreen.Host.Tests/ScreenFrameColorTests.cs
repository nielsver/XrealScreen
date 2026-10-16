using XrealScreen.Host;

namespace XrealScreen.Host.Tests;

public class ScreenFrameColorTests
{
    [Fact]
    public void LinearDefaults_MeetBetweenScreens()
    {
        Span<(byte R, byte G, byte B)> slots = stackalloc (byte R, byte G, byte B)[6];
        ScreenFrameColors.WriteDefaults(slots, (0, 0, 0), (255, 0, 0), 3, slice: true);
        Assert.Equal((0, 0, 0), slots[0]);
        Assert.Equal(slots[1], slots[2]);
        Assert.Equal(slots[3], slots[4]);
        Assert.Equal((255, 0, 0), slots[5]);
    }

    [Fact]
    public void OtherStyles_CopyTheSharedPairOntoEveryScreen()
    {
        Span<(byte R, byte G, byte B)> slots = stackalloc (byte R, byte G, byte B)[6];
        ScreenFrameColors.WriteDefaults(slots, (1, 2, 3), (4, 5, 6), 3, slice: false);
        Assert.Equal((1, 2, 3), slots[0]);
        Assert.Equal((4, 5, 6), slots[1]);
        Assert.Equal((1, 2, 3), slots[4]);
        Assert.Equal((4, 5, 6), slots[5]);
    }
}
