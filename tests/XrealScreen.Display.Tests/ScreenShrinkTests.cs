using XrealScreen.Display;

namespace XrealScreen.Display.Tests;

public class ScreenShrinkTests
{
    [Fact]
    public void Shrink_AveragesDownToTheMaxWidth()
    {
        var pixels = new byte[4 * 2 * 4];
        for (int y = 0; y < 2; y++)
        {
            for (int x = 0; x < 4; x++)
            {
                int pixel = ((y * 4) + x) * 4;
                pixels[pixel] = (byte)(x < 2 ? 200 : 0);
                pixels[pixel + 3] = 255;
            }
        }

        (byte[] shrunk, int width, int height) = ScreenCapture.Shrink(pixels, 4, 2, 2);

        Assert.Equal(2, width);
        Assert.Equal(1, height);
        Assert.Equal(200, shrunk[0]);
        Assert.Equal(0, shrunk[4]);
        Assert.Equal(255, shrunk[3]);
        Assert.Equal(255, shrunk[7]);
    }
}
