using System.Drawing;
using System.Windows.Forms;

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
        lock (_gate)
        {
            (target, radius, color, text, hand) = (_target, _radius, _targetColor, _text, _handPoint);
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
    }

    private sealed class BufferedForm : Form
    {
        public BufferedForm() => DoubleBuffered = true;
    }
}
