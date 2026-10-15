using System.CommandLine;
using System.Diagnostics;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;
using XrealScreen.Display;
using XrealScreen.Render;
using XrealScreen.Render.Capture;

namespace XrealScreen.Cli;

/// <summary>Capture tests (M2 WGC spike: statistics only) and glasses screenshots.</summary>
internal static class CaptureCommands
{
    public static Command Create()
    {
        var capture = new Command("capture", "Capture tests (Windows.Graphics.Capture) and glasses screenshots.");
        capture.Subcommands.Add(CreateTest());
        capture.Subcommands.Add(CreateGlasses());
        return capture;
    }

    private static Command CreateGlasses()
    {
        var folder = new Option<string?>("--out") { Description = @"Folder (default: Pictures\XrealScreen)." };
        var command = new Command("glasses", "Save a screenshot of what the glasses show (PNG).") { folder };
        command.SetAction(async (parse, ct) =>
        {
            try
            {
                Console.WriteLine(await XrealScreen.Host.GlassesScreenshot.SaveAsync(parse.GetValue(folder), ct).ConfigureAwait(false));
                return 0;
            }
            catch (InvalidOperationException ex)
            {
                Console.Error.WriteLine(ex.Message);
                return 2;
            }
        });
        return command;
    }

    private static Command CreateTest()
    {
        var gdi = new Argument<string?>("gdi") { Description = @"Monitor like \\.\DISPLAY22 (default: first virtual monitor, else the glasses).", Arity = ArgumentArity.ZeroOrOne };
        var seconds = new Option<double>("--seconds") { DefaultValueFactory = _ => 3 };
        var border = new Option<bool>("--border") { Description = "Allow the yellow capture border (skip borderless request)." };
        var png = new Option<string?>("--png") { Description = "Write the first captured frame to this PNG path." };
        var command = new Command("test", "Capture a monitor for a few seconds and print frame statistics.") { gdi, seconds, border, png };
        command.SetAction(async (parse, ct) =>
        {
            var monitors = new CcdDisplayTopology().GetActiveMonitors();
            var target = parse.GetValue(gdi) is { } name
                ? monitors.FirstOrDefault(m => string.Equals(m.GdiDeviceName, name, StringComparison.OrdinalIgnoreCase))
                : monitors.FirstOrDefault(m => m.EdidManufacturer == "CHY") ?? GlassesDisplayLocator.FindGlasses(monitors);
            if (target is null)
            {
                Console.Error.WriteLine("Monitor not found.");
                return 2;
            }

            if (!MonitorCapture.IsSupported)
            {
                Console.Error.WriteLine("Windows.Graphics.Capture is not supported here.");
                return 3;
            }

            bool borderless = !parse.GetValue(border);
            if (borderless)
            {
                Console.WriteLine($"borderless capture allowed: {await MonitorCapture.RequestBorderlessAsync().ConfigureAwait(false)}");
            }

            IntPtr hmon = MonitorCapture.MonitorAt(target.X, target.Y);
            Console.WriteLine($"capturing {target.GdiDeviceName} {target.FriendlyName} {target.Resolution} (HMONITOR 0x{hmon:X})");

            using var gd = GraphicsDevice.Create();
            using var staging = gd.Device.CreateTexture2D(new Texture2DDescription(
                Format.B8G8R8A8_UNorm, 64, 64, 1, 1, BindFlags.None, ResourceUsage.Staging, CpuAccessFlags.Read));
            using var capture = new MonitorCapture(gd.Device, hmon, borderless);
            string? pngPath = parse.GetValue(png);

            long frames = 0;
            double latencySumMs = 0, latencyMaxMs = 0;
            string? sample = null;
            byte[]? savedFrame = null;
            int savedWidth = 0, savedHeight = 0;
            var sizes = new HashSet<string>();
            capture.FrameArrived += f =>
            {
                double nowMs = Stopwatch.GetTimestamp() * 1000.0 / Stopwatch.Frequency;
                double latency = nowMs - f.SystemRelativeTime.TotalMilliseconds;
                latencySumMs += latency;
                latencyMaxMs = Math.Max(latencyMaxMs, latency);
                sizes.Add($"{f.Width}×{f.Height}");
                if (frames++ == 0)
                {
                    sample = MeanColor(gd, staging, f);
                    if (pngPath is not null)
                    {
                        savedFrame = CopyFrame(gd, f);
                        savedWidth = f.Width;
                        savedHeight = f.Height;
                    }
                }
            };

            var sw = Stopwatch.StartNew();
            capture.Start();
            await Task.Delay(TimeSpan.FromSeconds(parse.GetValue(seconds)), ct).ConfigureAwait(false);
            capture.Dispose();

            Console.WriteLine($"frames={frames} in {sw.Elapsed.TotalSeconds:F1}s ({frames / sw.Elapsed.TotalSeconds:F1}/s; WGC only delivers frames when content changes)");
            if (frames > 0)
            {
                Console.WriteLine($"latency capture→callback avg={latencySumMs / frames:F1} ms max={latencyMaxMs:F1} ms; sizes={string.Join(",", sizes)}");
                Console.WriteLine($"center 64×64 mean BGRA = {sample}");
            }

            if (pngPath is not null)
            {
                if (savedFrame is null)
                {
                    Console.Error.WriteLine("no frame to write");
                    return 4;
                }

                await WritePngAsync(pngPath, savedFrame, savedWidth, savedHeight, ct).ConfigureAwait(false);
                Console.WriteLine($"wrote {pngPath} ({savedWidth}×{savedHeight})");
            }

            return frames > 0 ? 0 : 4;
        });
        return command;
    }

    /// <summary>Copy one captured frame to tightly packed BGRA. The source texture is only valid inside the callback.</summary>
    private static byte[] CopyFrame(GraphicsDevice gd, CapturedFrame frame)
    {
        using var staging = gd.Device.CreateTexture2D(new Texture2DDescription(
            Format.B8G8R8A8_UNorm, (uint)frame.Width, (uint)frame.Height, 1, 1, BindFlags.None, ResourceUsage.Staging, CpuAccessFlags.Read));
        gd.Context.CopySubresourceRegion(
            staging, 0, 0, 0, 0, frame.Texture, 0,
            new Vortice.Mathematics.Box(0, 0, 0, frame.Width, frame.Height, 1));
        var map = gd.Context.Map(staging, 0, MapMode.Read, Vortice.Direct3D11.MapFlags.None);
        try
        {
            byte[] pixels = new byte[frame.Width * frame.Height * 4];
            int rowBytes = frame.Width * 4;
            unsafe
            {
                fixed (byte* destination = pixels)
                {
                    for (int row = 0; row < frame.Height; row++)
                    {
                        System.Buffer.MemoryCopy(
                            (byte*)map.DataPointer + (row * map.RowPitch),
                            destination + (row * rowBytes),
                            rowBytes,
                            rowBytes);
                    }
                }
            }

            return pixels;
        }
        finally
        {
            gd.Context.Unmap(staging, 0);
        }
    }

    private static async Task WritePngAsync(string path, byte[] bgra, int width, int height, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        using var memory = new InMemoryRandomAccessStream();
        var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, memory).AsTask(cancellationToken).ConfigureAwait(false);
        encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Straight, (uint)width, (uint)height, 96, 96, bgra);
        await encoder.FlushAsync().AsTask(cancellationToken).ConfigureAwait(false);
        memory.Seek(0);
        await using var file = File.Create(path);
        await memory.AsStreamForRead().CopyToAsync(file, cancellationToken).ConfigureAwait(false);
    }

    private static string MeanColor(GraphicsDevice gd, ID3D11Texture2D staging, CapturedFrame frame)
    {
        int x = Math.Max(0, frame.Width / 2 - 32), y = Math.Max(0, frame.Height / 2 - 32);
        gd.Context.CopySubresourceRegion(staging, 0, 0, 0, 0, frame.Texture, 0, new Vortice.Mathematics.Box(x, y, 0, x + 64, y + 64, 1));
        var map = gd.Context.Map(staging, 0, MapMode.Read, Vortice.Direct3D11.MapFlags.None);
        try
        {
            long b = 0, g = 0, r = 0, a = 0;
            unsafe
            {
                for (int row = 0; row < 64; row++)
                {
                    byte* p = (byte*)map.DataPointer + row * map.RowPitch;
                    for (int col = 0; col < 64; col++, p += 4)
                    {
                        b += p[0];
                        g += p[1];
                        r += p[2];
                        a += p[3];
                    }
                }
            }

            const int n = 64 * 64;
            return $"({b / n},{g / n},{r / n},{a / n})";
        }
        finally
        {
            gd.Context.Unmap(staging, 0);
        }
    }
}
