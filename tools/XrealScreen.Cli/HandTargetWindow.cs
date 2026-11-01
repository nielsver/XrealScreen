using System.Drawing;
using System.Windows.Forms;
using XrealScreen.Device.XrealOne;
using XrealScreen.Vision.Hands;

namespace XrealScreen.Cli;

/// <summary>
/// Full-screen window on the glasses for `xrs hands control --calibrate` and `xrs hands targets`: shows one target
/// (calibration dot or click target) and a status line; counts real mouse clicks (sent by hand control) on it.
/// Runs on its own UI thread; all public methods may be called from any thread.
/// </summary>
internal sealed class HandTargetWindow : IDisposable
{
    private readonly Form _form;
    private readonly object _gate = new();
    private PointF? _target;
    private float _radius;
    private Color _targetColor = Color.Orange;
    private string _text = "";
    private Point? _handPoint;
    private Bitmap? _inset;
    private float _pinch = float.NaN;
    private bool _pressed;

    private HandTargetWindow(Form form) => _form = form;

    /// <summary>Clicks on the window (in window pixels) and whether they hit the current target.</summary>
    public event Action<Point, bool>? Clicked;

    public static HandTargetWindow Open(Rectangle bounds, Action onClosed)
    {
        HandTargetWindow? window = null;
        using var ready = new ManualResetEventSlim();
        var thread = new Thread(() =>
        {
            Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
            var form = new BufferedForm
            {
                FormBorderStyle = FormBorderStyle.None,
                StartPosition = FormStartPosition.Manual,
                Bounds = bounds,
                BackColor = Color.FromArgb(16, 16, 20),
                TopMost = true,
                Text = "xrs hands",
                KeyPreview = true,
            };
            window = new HandTargetWindow(form);
            form.Paint += (_, e) => window.Paint(e.Graphics);
            form.MouseDown += (_, e) => window.OnMouseDown(e);
            form.KeyDown += (_, e) =>
            {
                if (e.KeyCode == Keys.Escape)
                {
                    form.Close();
                }
            };
            form.FormClosed += (_, _) => onClosed();
            form.Shown += (_, _) => ready.Set();
            Application.Run(form);
        })
        { IsBackground = true, Name = "Hand targets" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        ready.Wait(TimeSpan.FromSeconds(10));
        return window ?? throw new InvalidOperationException("Target window did not open.");
    }

    public Size ClientSize => _form.ClientSize;

    /// <summary>Shows a target at (u, v) of the window (0..1) with the given diameter in pixels; null hides it.</summary>
    public void ShowTarget(double? u, double? v, float diameter, Color color)
    {
        lock (_gate)
        {
            _target = u is null || v is null ? null : new PointF((float)(u * ClientSize.Width), (float)(v * ClientSize.Height));
            _radius = diameter / 2;
            _targetColor = color;
        }

        Invalidate();
    }

    public void SetText(string text)
    {
        lock (_gate)
        {
            _text = text;
        }

        Invalidate();
    }

    /// <summary>Where hand control currently points (window pixels), drawn as a ring; null hides it.</summary>
    public void SetHandPoint(Point? windowPoint)
    {
        lock (_gate)
        {
            _handPoint = windowPoint;
        }

        Invalidate();
    }

    /// <summary>
    /// Camera view in the corner (camera image, hand skeleton, the knuckle that steers the pointer) and the pinch
    /// meter, so the user sees how the hand is seen (user request, E3 test 2026-11-01). Copies what it needs.
    /// </summary>
    public void SetHand(EyeFrame? camera, HandResult? hand, float pinchRatio, bool pressed, HandCalibration? calibration)
    {
        Bitmap? inset = camera is null ? null : DrawInset(camera, hand, pressed, calibration);
        Bitmap? old;
        lock (_gate)
        {
            old = _inset;
            _inset = inset ?? _inset;
            _pinch = pinchRatio;
            _pressed = pressed;
        }

        if (inset is not null)
        {
            old?.Dispose();
        }

        Invalidate();
    }

    private const int InsetWidth = 378;

    private static Bitmap DrawInset(EyeFrame frame, HandResult? hand, bool pressed, HandCalibration? calibration)
    {
        float scale = (float)InsetWidth / frame.Width;
        var bmp = new Bitmap(InsetWidth, (int)(frame.Height * scale), System.Drawing.Imaging.PixelFormat.Format24bppRgb);
        var data = bmp.LockBits(new Rectangle(0, 0, bmp.Width, bmp.Height), System.Drawing.Imaging.ImageLockMode.WriteOnly, bmp.PixelFormat);
        var row = new byte[data.Stride];
        var pixels = frame.Pixels;
        for (int y = 0; y < bmp.Height; y++)
        {
            int sy = Math.Min(frame.Height - 1, (int)(y / scale));
            for (int x = 0; x < bmp.Width; x++)
            {
                byte v = pixels[sy * frame.Stride + Math.Min(frame.Width - 1, (int)(x / scale))];
                row[3 * x] = row[3 * x + 1] = row[3 * x + 2] = (byte)(v * 3 / 4);
            }

            System.Runtime.InteropServices.Marshal.Copy(row, 0, data.Scan0 + y * data.Stride, row.Length);
        }

        bmp.UnlockBits(data);
        using (var g = Graphics.FromImage(bmp))
        {
            // the camera area that covers the screen: keep the knuckle inside it
            var c = calibration ?? HandCalibration.Default(frame.Width, frame.Height);
            var quad = new[] { (0.0, 0.0), (1.0, 0.0), (1.0, 1.0), (0.0, 1.0) }
                .Select(p => { var (x, y) = c.Inverse(p.Item1, p.Item2); return new PointF((float)x * scale, (float)y * scale); }).ToArray();
            using var area = new Pen(Color.FromArgb(160, 255, 255, 255), 1) { DashStyle = System.Drawing.Drawing2D.DashStyle.Dash };
            g.DrawPolygon(area, quad);
        }

        if (hand is not null)
        {
            using var g = Graphics.FromImage(bmp);
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            using var bone = new Pen(Color.FromArgb(0, 200, 255), 2);
            foreach (var (a, b) in HandPreviewWindow.Bones)
            {
                g.DrawLine(bone, hand.Landmarks[a].X * scale, hand.Landmarks[a].Y * scale, hand.Landmarks[b].X * scale, hand.Landmarks[b].Y * scale);
            }

            using var tips = new SolidBrush(pressed ? Color.LimeGreen : Color.Orange);
            foreach (int j in new[] { HandJoint.ThumbTip, HandJoint.IndexTip })
            {
                g.FillEllipse(tips, hand.Landmarks[j].X * scale - 5, hand.Landmarks[j].Y * scale - 5, 10, 10);
            }

            var k = hand.Landmarks[HandJoint.IndexBase];
            using var knuckle = new Pen(Color.White, 3);
            g.DrawEllipse(knuckle, k.X * scale - 9, k.Y * scale - 9, 18, 18);
        }

        return bmp;
    }

    private void Invalidate()
    {
        try
        {
            if (!_form.IsDisposed)
            {
                _form.BeginInvoke(_form.Invalidate);
            }
        }
        catch (InvalidOperationException)
        {
        }
    }

    private void OnMouseDown(MouseEventArgs e)
    {
        bool hit;
        lock (_gate)
        {
            hit = _target is { } t && (e.X - t.X) * (e.X - t.X) + (e.Y - t.Y) * (e.Y - t.Y) <= _radius * _radius;
        }

        Clicked?.Invoke(e.Location, hit);
    }

    private void Paint(Graphics g)
    {
        PointF? target;
        float radius;
        Color color;
        string text;
        Point? hand;
        float pinch;
        bool pressed;
        lock (_gate)
        {
            (target, radius, color, text, hand, pinch, pressed) = (_target, _radius, _targetColor, _text, _handPoint, _pinch, _pressed);
            if (_inset is { } inset)
            {
                // camera view bottom-right, pinch meter above it
                int x = _form.ClientSize.Width - inset.Width - 20, y = _form.ClientSize.Height - inset.Height - 20;
                g.DrawImage(inset, x, y);
                g.DrawRectangle(Pens.Gray, x - 1, y - 1, inset.Width + 1, inset.Height + 1);
                DrawPinchMeter(g, new Rectangle(x, y - 70, inset.Width, 26), pinch, pressed);
            }
        }

        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        using var font = new Font(FontFamily.GenericSansSerif, 22);
        g.DrawString(text, font, Brushes.White, new RectangleF(40, 30, _form.ClientSize.Width - 80, 200));
        if (target is { } t)
        {
            using var brush = new SolidBrush(color);
            g.FillEllipse(brush, t.X - radius, t.Y - radius, 2 * radius, 2 * radius);
            g.FillEllipse(Brushes.White, t.X - 3, t.Y - 3, 6, 6);
        }

        if (hand is { } h)
        {
            using var pen = new Pen(Color.FromArgb(0, 200, 255), 3);
            g.DrawEllipse(pen, h.X - 14, h.Y - 14, 28, 28);
        }
    }

    /// <summary>Bar from "fingers together" (left) to "open" (right); the marker is where a pinch counts.</summary>
    private static void DrawPinchMeter(Graphics g, Rectangle r, float ratio, bool pressed)
    {
        const float max = 1.2f;
        float close = new PinchDetector().CloseRatio;
        using var font = new Font(FontFamily.GenericSansSerif, 13);
        g.DrawString(float.IsNaN(ratio) ? "pinch: no hand" : pressed ? "PINCH" : "pinch: bring thumb and index together", font, pressed ? Brushes.LimeGreen : Brushes.White, r.X, r.Y - 26);
        g.FillRectangle(Brushes.DimGray, r);
        if (!float.IsNaN(ratio))
        {
            int w = (int)(r.Width * Math.Clamp(1 - ratio / max, 0, 1));
            using var fill = new SolidBrush(pressed ? Color.LimeGreen : Color.Orange);
            g.FillRectangle(fill, r.X, r.Y, w, r.Height);
        }

        // fill grows to the right as the fingers close; the white marker is the "pinch counts" point
        int mx = r.X + (int)(r.Width * (1 - close / max));
        using var marker = new Pen(Color.White, 3);
        g.DrawLine(marker, mx, r.Y - 4, mx, r.Bottom + 4);
    }

    public void Dispose()
    {
        try
        {
            if (!_form.IsDisposed)
            {
                _form.BeginInvoke(_form.Close);
            }
        }
        catch (InvalidOperationException)
        {
        }

        lock (_gate)
        {
            _inset?.Dispose();
            _inset = null;
        }
    }

    private sealed class BufferedForm : Form
    {
        public BufferedForm() => DoubleBuffered = true;
    }
}
