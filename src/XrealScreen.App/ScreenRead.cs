using System.Drawing;
using System.Runtime.InteropServices;
using Windows.Foundation;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using XrealScreen.Core.Hud;
using XrealScreen.Display;

namespace XrealScreen.App;

/// <summary>One glance: capture the main monitor, recognize the text, and ask for a short note when a key is set.</summary>
internal static class ScreenRead
{
    private static readonly OcrEngine? Engine = OcrEngine.TryCreateFromUserProfileLanguages();
    public static async Task<string> ReadAsync(CancellationToken cancellationToken)
    {
        string recognized = await RecognizePrimaryAsync(cancellationToken).ConfigureAwait(false);
        string shown = GlancePrompt.ForHud(recognized);
        string? apiKey = Environment.GetEnvironmentVariable("XAI_API_KEY");
        if (string.IsNullOrWhiteSpace(apiKey) || shown == "NO TEXT")
        {
            return shown;
        }

        try
        {
            string? answer = await GlanceAiClient.CompleteAsync(apiKey, recognized, cancellationToken).ConfigureAwait(false);
            return string.IsNullOrWhiteSpace(answer) ? shown : GlancePrompt.ForHud(answer);
        }
        catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException or TaskCanceledException or System.Text.Json.JsonException)
        {
            return shown;
        }
    }

    private static async Task<string> RecognizePrimaryAsync(CancellationToken cancellationToken)
    {
        var monitors = new CcdDisplayTopology().GetActiveMonitors();
        var monitor = ReadingMonitor.Choose(monitors) ?? throw new InvalidOperationException("No screen to read.");
        var bounds = new Rectangle(monitor.X, monitor.Y, monitor.Resolution.Width, monitor.Resolution.Height);
        byte[] pixels = await Task.Run(() => ScreenCapture.CaptureBgra(bounds), cancellationToken).ConfigureAwait(false);
        (pixels, int width, int height) = Shrink(pixels, bounds.Width, bounds.Height, 1600);
        try
        {
            return await RecognizeAsync(pixels, width, height, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return string.Empty;
        }
    }

    private static async Task<string> RecognizeAsync(byte[] pixels, int width, int height, CancellationToken cancellationToken)
    {
        using var bitmap = new SoftwareBitmap(BitmapPixelFormat.Bgra8, width, height, BitmapAlphaMode.Ignore);
        WritePixels(bitmap, pixels, width, height);
        if (Engine is null)
        {
            return string.Empty;
        }

        OcrResult result = await Engine.RecognizeAsync(bitmap).AsTask(cancellationToken).ConfigureAwait(false);
        return result.Text ?? string.Empty;
    }

    private static unsafe void WritePixels(SoftwareBitmap bitmap, byte[] pixels, int width, int height)
    {
        using BitmapBuffer buffer = bitmap.LockBuffer(BitmapBufferAccessMode.Write);
        using IMemoryBufferReference reference = buffer.CreateReference();
        BitmapPlaneDescription plane = buffer.GetPlaneDescription(0);
        ((IMemoryBufferByteAccess)reference).GetBuffer(out byte* data, out uint capacity);
        int rowBytes = width * 4;
        if ((long)plane.Stride * height > capacity)
        {
            throw new InvalidOperationException("The screen buffer is too small.");
        }

        for (int y = 0; y < height; y++)
        {
            Marshal.Copy(pixels, y * rowBytes, (nint)(data + (y * plane.Stride)), rowBytes);
        }
    }

    private static (byte[] Pixels, int Width, int Height) Shrink(byte[] pixels, int width, int height, int maxWidth)
    {
        int factor = Math.Max(1, width / maxWidth);
        if (factor == 1)
        {
            return (pixels, width, height);
        }

        int newWidth = width / factor;
        int newHeight = height / factor;
        var destination = new byte[newWidth * newHeight * 4];
        int sample = factor * factor;
        for (int y = 0; y < newHeight; y++)
        {
            for (int x = 0; x < newWidth; x++)
            {
                int blue = 0;
                int green = 0;
                int red = 0;
                for (int oy = 0; oy < factor; oy++)
                {
                    int sourceY = (y * factor) + oy;
                    for (int ox = 0; ox < factor; ox++)
                    {
                        int source = (((sourceY * width) + (x * factor) + ox) * 4);
                        blue += pixels[source];
                        green += pixels[source + 1];
                        red += pixels[source + 2];
                    }
                }

                int target = ((y * newWidth) + x) * 4;
                destination[target] = (byte)(blue / sample);
                destination[target + 1] = (byte)(green / sample);
                destination[target + 2] = (byte)(red / sample);
                destination[target + 3] = 255;
            }
        }

        return (destination, newWidth, newHeight);
    }

    [ComImport]
    [Guid("5B0D3235-4DBA-4D44-865E-8F1D0E4FD04D")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMemoryBufferByteAccess
    {
        unsafe void GetBuffer(out byte* buffer, out uint capacity);
    }
}
