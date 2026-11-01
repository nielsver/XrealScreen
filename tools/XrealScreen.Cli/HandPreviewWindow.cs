using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using XrealScreen.Device.XrealOne;
using XrealScreen.Vision.Hands;

namespace XrealScreen.Cli;

/// <summary>`xrs hands live --window`: camera image (2× size) with the tracked hand skeleton, on its own UI thread.</summary>
internal sealed class HandPreviewWindow : IDisposable
{
    private const int Zoom = 2;

    internal static readonly (int A, int B)[] Bones =
    [
        (0, 1), (1, 2), (2, 3), (3, 4), (0, 5), (5, 6), (6, 7), (7, 8), (5, 9), (9, 10), (10, 11), (11, 12),
        (9, 13), (13, 14), (14, 15), (15, 16), (13, 17), (17, 18), (18, 19), (19, 20), (0, 17),
    ];

    private readonly Form _form;
    private readonly PictureBox _picture;
    private int _busy;

    private HandPreviewWindow(Form form, PictureBox picture)
    {
        _form = form;
        _picture = picture;
    }

    public static HandPreviewWindow Start(Action onClosed)
    {
        HandPreviewWindow? window = null;
        using var ready = new ManualResetEventSlim();
        var thread = new Thread(() =>
        {
            Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
            var picture = new PictureBox { Dock = DockStyle.Fill, SizeMode = PictureBoxSizeMode.Zoom, BackColor = Color.Black };
            var form = new Form { Text = "xrs hands live — camera image stays on this PC", ClientSize = new Size(504 * Zoom, 378 * Zoom) };
            form.Controls.Add(picture);
            form.FormClosed += (_, _) => onClosed();
            window = new HandPreviewWindow(form, picture);
            form.Shown += (_, _) => ready.Set();
            Application.Run(form);
        })
        { IsBackground = true, Name = "Hand preview" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        ready.Wait(TimeSpan.FromSeconds(10));
        return window ?? throw new InvalidOperationException("Preview window did not open.");
    }

    /// <summary>Draws the frame; skipped while the previous one is still being shown.</summary>
    public void Show(EyeFrame frame, HandResult? hand, bool pressed)
    {
        if (Interlocked.Exchange(ref _busy, 1) == 1 || _form.IsDisposed)
        {
            return;
        }

        var bitmap = Draw(frame, hand, pressed);
        try
        {
            _form.BeginInvoke(() =>
            {
                var old = _picture.Image;
                _picture.Image = bitmap;
                old?.Dispose();
                Volatile.Write(ref _busy, 0);
            });
        }
        catch (InvalidOperationException)
        {
            bitmap.Dispose();
        }
    }

    private static Bitmap Draw(EyeFrame frame, HandResult? hand, bool pressed)
    {
        var gray = new Bitmap(frame.Width, frame.Height, PixelFormat.Format24bppRgb);
        var data = gray.LockBits(new Rectangle(0, 0, frame.Width, frame.Height), ImageLockMode.WriteOnly, PixelFormat.Format24bppRgb);
        var row = new byte[data.Stride];
        var pixels = frame.Pixels;
        for (int y = 0; y < frame.Height; y++)
        {
            var src = pixels.Slice(y * frame.Stride, frame.Width);
            for (int x = 0; x < frame.Width; x++)
            {
                row[3 * x] = row[3 * x + 1] = row[3 * x + 2] = src[x];
            }

            Marshal.Copy(row, 0, data.Scan0 + y * data.Stride, row.Length);
        }

        gray.UnlockBits(data);
        var canvas = new Bitmap(frame.Width * Zoom, frame.Height * Zoom);
        using (var g = Graphics.FromImage(canvas))
        {
            g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.Bilinear;
            g.DrawImage(gray, 0, 0, canvas.Width, canvas.Height);
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            if (hand is not null)
            {
                using var bone = new Pen(Color.FromArgb(0, 200, 255), 2);
                foreach (var (a, b) in Bones)
                {
                    g.DrawLine(bone, hand.Landmarks[a].X * Zoom, hand.Landmarks[a].Y * Zoom, hand.Landmarks[b].X * Zoom, hand.Landmarks[b].Y * Zoom);
                }

                var (px, py) = hand.PinchPoint;
                using var dot = new SolidBrush(pressed ? Color.LimeGreen : Color.Orange);
                g.FillEllipse(dot, px * Zoom - 8, py * Zoom - 8, 16, 16);
                using var font = new Font(FontFamily.GenericSansSerif, 12);
                g.DrawString($"pinch {hand.PinchRatio:F2}{(pressed ? "  PRESSED" : "")}", font, dot, 8, 8);
            }
            else
            {
                using var font = new Font(FontFamily.GenericSansSerif, 12);
                g.DrawString("no hand", font, Brushes.Orange, 8, 8);
            }
        }

        gray.Dispose();
        return canvas;
    }

    public void Dispose()
    {
        if (!_form.IsDisposed)
        {
            try
            {
                _form.BeginInvoke(_form.Close);
            }
            catch (InvalidOperationException)
            {
            }
        }
    }
}
