namespace XrealScreen.Vision.Hands;

/// <summary>Image preparation for the hand models (grayscale in, RGB float tensors out).</summary>
internal static class ImageOps
{
    /// <summary>
    /// 3×3 Gaussian blur (1-2-1) of an 8-bit grayscale image into <paramref name="dst"/> (width × height floats).
    /// The Eye image is too sharp/noisy for the models without it (hand test 2026-10-29).
    /// </summary>
    public static void Blur3x3(ReadOnlySpan<byte> src, int width, int height, int stride, Span<float> dst)
    {
        if (dst.Length < width * height)
        {
            throw new ArgumentException("Destination is too small.", nameof(dst));
        }

        if (width < 2 || height < 2)
        {
            throw new ArgumentException("Image must be at least 2×2.");
        }

        // horizontal pass into dst (edges repeat the border pixel)
        for (int y = 0; y < height; y++)
        {
            var line = src.Slice(y * stride, width);
            var row = dst.Slice(y * width, width);
            row[0] = (3 * line[0] + line[1]) * 0.25f;
            for (int x = 1; x < width - 1; x++)
            {
                row[x] = (line[x - 1] + 2 * line[x] + line[x + 1]) * 0.25f;
            }

            row[width - 1] = (line[width - 2] + 3 * line[width - 1]) * 0.25f;
        }

        // vertical pass in place, keeping the unmodified previous row
        float[] previous = new float[width];
        float[] current = new float[width];
        dst[..width].CopyTo(previous);
        for (int y = 0; y < height; y++)
        {
            var row = dst.Slice(y * width, width);
            row.CopyTo(current);
            var next = y + 1 < height ? dst.Slice((y + 1) * width, width) : current;
            for (int x = 0; x < width; x++)
            {
                row[x] = (previous[x] + 2 * current[x] + next[x]) * 0.25f;
            }

            (previous, current) = (current, previous);
        }
    }

    /// <summary>
    /// Samples the square <paramref name="roi"/> of a grayscale float image into an NHWC RGB tensor of
    /// <paramref name="size"/>² pixels with values 0..1 (bilinear, black outside the image).
    /// </summary>
    public static void SampleToTensor(ReadOnlySpan<float> image, int width, int height, RotatedRect roi, int size, Span<float> tensor)
    {
        if (tensor.Length < size * size * 3)
        {
            throw new ArgumentException("Tensor is too small.", nameof(tensor));
        }

        float c = MathF.Cos(roi.Rotation);
        float s = MathF.Sin(roi.Rotation);
        for (int v = 0; v < size; v++)
        {
            float ny = ((v + 0.5f) / size - 0.5f) * roi.Size;
            for (int u = 0; u < size; u++)
            {
                float nx = ((u + 0.5f) / size - 0.5f) * roi.Size;
                float x = roi.CenterX + nx * c - ny * s - 0.5f;
                float y = roi.CenterY + nx * s + ny * c - 0.5f;
                float value = Bilinear(image, width, height, x, y) / 255f;
                int o = (v * size + u) * 3;
                tensor[o] = value;
                tensor[o + 1] = value;
                tensor[o + 2] = value;
            }
        }
    }

    private static float Bilinear(ReadOnlySpan<float> image, int width, int height, float x, float y)
    {
        int x0 = (int)MathF.Floor(x);
        int y0 = (int)MathF.Floor(y);
        float fx = x - x0;
        float fy = y - y0;
        return Pixel(image, width, height, x0, y0) * (1 - fx) * (1 - fy)
             + Pixel(image, width, height, x0 + 1, y0) * fx * (1 - fy)
             + Pixel(image, width, height, x0, y0 + 1) * (1 - fx) * fy
             + Pixel(image, width, height, x0 + 1, y0 + 1) * fx * fy;
    }

    private static float Pixel(ReadOnlySpan<float> image, int width, int height, int x, int y) =>
        (uint)x < (uint)width && (uint)y < (uint)height ? image[y * width + x] : 0f;
}
