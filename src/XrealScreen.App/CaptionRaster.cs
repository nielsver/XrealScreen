using System.Drawing;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using XrealScreen.Core.Hud;

namespace XrealScreen.App;

/// <summary>Draws the screen note with Segoe UI onto a small opaque card.</summary>
internal static class CaptionRaster
{
    public static HudCaptionPicture Draw(string text, int revision)
    {
        const int maxTextWidth = 860;
        const float fontPx = 32f;
        using Font font = CreateFont(fontPx);
        using var measure = new Bitmap(1, 1, PixelFormat.Format32bppArgb);
        using Graphics measureGraphics = Graphics.FromImage(measure);
        SizeF size = measureGraphics.MeasureString(text, font, maxTextWidth);
        int width = Math.Clamp((int)Math.Ceiling(size.Width) + 36, 64, maxTextWidth + 36);
        int height = Math.Clamp((int)Math.Ceiling(size.Height) + 28, 48, 220);
        using var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        using Graphics draw = Graphics.FromImage(bitmap);
        draw.Clear(Color.FromArgb(255, 6, 14, 16));
        draw.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        using var brush = new SolidBrush(Color.FromArgb(255, 186, 244, 255));
        draw.DrawString(text, font, brush, new RectangleF(18, 12, width - 36, height - 24));

        var pixels = new byte[width * height * 4];
        BitmapData data = bitmap.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            for (int y = 0; y < height; y++)
            {
                Marshal.Copy(data.Scan0 + (y * data.Stride), pixels, y * width * 4, width * 4);
            }
        }
        finally
        {
            bitmap.UnlockBits(data);
        }

        for (int i = 3; i < pixels.Length; i += 4)
        {
            pixels[i] = 255;
        }

        return new HudCaptionPicture(revision, width, height, pixels);
    }

    private static Font CreateFont(float size)
    {
        try
        {
            return new Font("Segoe UI", size, FontStyle.Regular, GraphicsUnit.Pixel);
        }
        catch (ArgumentException)
        {
            return new Font(FontFamily.GenericSansSerif, size, FontStyle.Regular, GraphicsUnit.Pixel);
        }
    }
}
