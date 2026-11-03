using XrealScreen.Vision.Hands;

namespace XrealScreen.Vision.Tests;

public class ImageOpsTests
{
    [Fact]
    public void Blur_ConstantImage_StaysConstant_AndSkipsRowPadding()
    {
        const int w = 6, h = 4, stride = 8;
        var src = new byte[stride * h];
        for (int y = 0; y < h; y++)
        {
            Array.Fill(src, (byte)80, y * stride, w);
            Array.Fill(src, (byte)255, y * stride + w, stride - w); // padding must not leak in
        }

        var dst = new float[w * h];
        ImageOps.Blur3x3(src, w, h, stride, dst);

        Assert.All(dst, v => Assert.Equal(80f, v, 4));
    }

    [Fact]
    public void Blur_SinglePixel_Spreads1_2_1()
    {
        const int w = 5, h = 5;
        var src = new byte[w * h];
        src[2 * w + 2] = 160;
        var dst = new float[w * h];

        ImageOps.Blur3x3(src, w, h, w, dst);

        Assert.Equal(40f, dst[2 * w + 2], 4);   // 160 * 2/4 * 2/4
        Assert.Equal(20f, dst[2 * w + 1], 4);   // 160 * 1/4 * 2/4
        Assert.Equal(10f, dst[1 * w + 1], 4);   // 160 * 1/4 * 1/4
    }

    [Fact]
    public void SampleToTensor_AxisAlignedRegion_CopiesPixelsAsRgb01()
    {
        const int w = 4, h = 4;
        var image = new float[w * h];
        for (int i = 0; i < image.Length; i++)
        {
            image[i] = i * 10;
        }

        var tensor = new float[4 * 4 * 3];
        ImageOps.SampleToTensor(image, w, h, new RotatedRect(2, 2, 4, 0), 4, tensor);

        Assert.Equal(image[1 * w + 2] / 255f, tensor[(1 * 4 + 2) * 3], 4);
        Assert.Equal(tensor[0], tensor[1]);
        Assert.Equal(tensor[0], tensor[2]);
    }

    [Fact]
    public void SampleToTensor_OutsideTheImage_IsBlack()
    {
        var image = new float[4 * 4];
        Array.Fill(image, 255f);
        var tensor = new float[4 * 4 * 3];

        ImageOps.SampleToTensor(image, 4, 4, new RotatedRect(100, 100, 4, 0), 4, tensor);

        Assert.All(tensor, v => Assert.Equal(0f, v));
    }

    [Fact]
    public void SampleToTensor_Rotated90_TurnsTheImage()
    {
        // a bright column at x = 3 (right edge); rotated by +90° the crop's top row looks along it
        const int w = 4, h = 4;
        var image = new float[w * h];
        for (int y = 0; y < h; y++)
        {
            image[y * w + 3] = 255;
        }

        var tensor = new float[4 * 4 * 3];
        ImageOps.SampleToTensor(image, w, h, new RotatedRect(2, 2, 4, MathF.PI / 2), 4, tensor);

        // with +90° the crop's top row (v = 0) lies on the image's right column
        float topRowMean = Enumerable.Range(0, 4).Average(u => tensor[u * 3]);
        float bottomRowMean = Enumerable.Range(0, 4).Average(u => tensor[(3 * 4 + u) * 3]);
        Assert.True(topRowMean > bottomRowMean);
    }
}
