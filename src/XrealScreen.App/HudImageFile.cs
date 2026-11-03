using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace XrealScreen.App;

/// <summary>One image the HUD can draw, decoded once. The bytes stay immutable.</summary>
internal sealed class HudImageItem
{
    public required int Id { get; init; }

    public required string Path { get; init; }

    public required string Name { get; init; }

    public required byte[] Bgra { get; init; }

    public required int Width { get; init; }

    public required int Height { get; init; }

    public required int Revision { get; init; }

    public float CenterX { get; set; }

    public float CenterY { get; set; }

    public float Size { get; set; } = 0.4f;
}

/// <summary>Loads a picture file into top-down BGRA. The long edge is at most <see cref="MaxEdge"/>.</summary>
internal static class HudImageFile
{
    public const int MaxCount = 12;

    public const int MaxEdge = 960;

    public static HudImageItem? Load(string path, int id, int revision, float centerX, float centerY, float size)
    {
        try
        {
            using var source = new Bitmap(path);
            TryHonorOrientation(source);
            int width = source.Width;
            int height = source.Height;
            if (width <= 0 || height <= 0)
            {
                return null;
            }

            using var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
            using (var graphics = Graphics.FromImage(bitmap))
            {
                graphics.DrawImage(source, 0, 0, width, height);
            }

            var data = bitmap.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            byte[] pixels;
            try
            {
                int rowBytes = width * 4;
                pixels = new byte[rowBytes * height];
                nint row = data.Scan0;
                for (int y = 0; y < height; y++)
                {
                    Marshal.Copy(row, pixels, y * rowBytes, rowBytes);
                    row += data.Stride;
                }
            }
            finally
            {
                bitmap.UnlockBits(data);
            }

            bool transparent = false;
            for (int i = 3; i < pixels.Length; i += 4)
            {
                if (pixels[i] != 0)
                {
                    transparent = true;
                    break;
                }
            }

            if (!transparent)
            {
                for (int i = 3; i < pixels.Length; i += 4)
                {
                    pixels[i] = 255;
                }
            }

            (pixels, width, height) = Fit(pixels, width, height, MaxEdge);
            return new HudImageItem
            {
                Id = id,
                Path = path,
                Name = System.IO.Path.GetFileName(path),
                Bgra = pixels,
                Width = width,
                Height = height,
                Revision = revision,
                CenterX = centerX,
                CenterY = centerY,
                Size = size,
            };
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or UnauthorizedAccessException or OutOfMemoryException or ExternalException or NotSupportedException)
        {
            return null;
        }
    }

    private static void TryHonorOrientation(Bitmap source)
    {
        try
        {
            const int orientation = 0x0112;
            if (!source.PropertyIdList.Contains(orientation))
            {
                return;
            }

            byte[]? value = source.GetPropertyItem(orientation)?.Value;
            switch (value is { Length: > 0 } ? value[0] : 0)
            {
                case 6:
                    source.RotateFlip(RotateFlipType.Rotate90FlipNone);
                    break;
                case 3:
                    source.RotateFlip(RotateFlipType.Rotate180FlipNone);
                    break;
                case 8:
                    source.RotateFlip(RotateFlipType.Rotate270FlipNone);
                    break;
            }
        }
        catch (Exception ex) when (ex is ArgumentException or ExternalException or NotSupportedException)
        {
        }
    }

    private static (byte[] Pixels, int Width, int Height) Fit(byte[] pixels, int width, int height, int maxEdge)
    {
        int edge = Math.Max(width, height);
        if (edge <= maxEdge)
        {
            return (pixels, width, height);
        }

        int factor = Math.Max(1, (edge + maxEdge - 1) / maxEdge);
        int newWidth = Math.Max(1, width / factor);
        int newHeight = Math.Max(1, height / factor);
        var destination = new byte[newWidth * newHeight * 4];
        for (int y = 0; y < newHeight; y++)
        {
            for (int x = 0; x < newWidth; x++)
            {
                int blue = 0;
                int green = 0;
                int red = 0;
                int alpha = 0;
                int count = 0;
                for (int oy = 0; oy < factor; oy++)
                {
                    int sourceY = (y * factor) + oy;
                    if (sourceY >= height)
                    {
                        break;
                    }

                    for (int ox = 0; ox < factor; ox++)
                    {
                        int sourceX = (x * factor) + ox;
                        if (sourceX >= width)
                        {
                            break;
                        }

                        int source = (((sourceY * width) + sourceX) * 4);
                        blue += pixels[source];
                        green += pixels[source + 1];
                        red += pixels[source + 2];
                        alpha += pixels[source + 3];
                        count++;
                    }
                }

                if (count == 0)
                {
                    continue;
                }

                int target = ((y * newWidth) + x) * 4;
                destination[target] = (byte)(blue / count);
                destination[target + 1] = (byte)(green / count);
                destination[target + 2] = (byte)(red / count);
                destination[target + 3] = (byte)(alpha / count);
            }
        }

        return (destination, newWidth, newHeight);
    }
}
