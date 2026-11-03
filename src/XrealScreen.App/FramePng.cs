using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace XrealScreen.App;

/// <summary>Writes a BGRA frame to a PNG the Grok CLI can open.</summary>
internal static class FramePng
{
    public static void Save(byte[] bgra, int width, int height, string path)
    {
        ArgumentNullException.ThrowIfNull(bgra);
        ArgumentException.ThrowIfNullOrEmpty(path);
        using var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        BitmapData data = bitmap.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
        try
        {
            int rowBytes = width * 4;
            for (int y = 0; y < height; y++)
            {
                Marshal.Copy(bgra, y * rowBytes, data.Scan0 + (y * data.Stride), rowBytes);
            }
        }
        finally
        {
            bitmap.UnlockBits(data);
        }

        string? directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        bitmap.Save(path, ImageFormat.Png);
    }
}
