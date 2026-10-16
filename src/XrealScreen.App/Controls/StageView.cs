using System.ComponentModel;
using System.Globalization;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using XrealScreen.App.ViewModels;
using XrealScreen.Core.Workspace;

namespace XrealScreen.App.Controls;

/// <summary>
/// Stage look: the screens as you will see them, floating in a dark room. Head tracking turns each screen
/// towards you at its yaw; glasses anchor shows the screens side by side in the glasses image.
/// </summary>
public sealed partial class StageView : UserControl
{
    private const double DegreesAcross = 150; // head tracking: the scale for a narrow arc (it never zooms in further than 1.4×)

    private const double MaxTilt = 32; // degrees a side screen is drawn turned

    private readonly Canvas _canvas = new();
    private WorkspaceViewModel? _workspace;
    private TrackingViewModel? _tracking;

    public StageView()
    {
        Content = _canvas;
        SizeChanged += (_, _) => Redraw();
        Loaded += (_, _) => Attach();
        Unloaded += (_, _) => Detach();
    }

    public Brush Face { get; set; } = new SolidColorBrush(Colors.WhiteSmoke);

    public Brush Accent { get; set; } = new SolidColorBrush(Colors.Orange);

    public Brush Muted { get; set; } = new SolidColorBrush(Colors.Gray);

    private void Attach()
    {
        _workspace = App.Workspace;
        _tracking = App.Tracking;
        _workspace.LayoutChanged += OnChanged;
        _workspace.AnchorPreviewChanged += OnChanged;
        _workspace.PropertyChanged += OnWorkspacePropertyChanged;
        _tracking.PoseUpdated += OnChanged;
        _workspace.RefreshGlassesSignal(); // computes the anchor layout; redraws through AnchorPreviewChanged
        Redraw();
    }

    private void Detach()
    {
        if (_workspace is not null)
        {
            _workspace.LayoutChanged -= OnChanged;
            _workspace.AnchorPreviewChanged -= OnChanged;
            _workspace.PropertyChanged -= OnWorkspacePropertyChanged;
        }

        if (_tracking is not null)
        {
            _tracking.PoseUpdated -= OnChanged;
        }
    }

    private void OnWorkspacePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(WorkspaceViewModel.KindIndex) or nameof(WorkspaceViewModel.PrimaryScreenIndex))
        {
            Redraw();
        }
    }

    private void OnChanged(object? sender, EventArgs e) => Redraw();

    private void Redraw()
    {
        _canvas.Children.Clear();
        if (_workspace is null || ActualWidth <= 0 || ActualHeight <= 0)
        {
            return;
        }

        DrawFloor();
        if (_workspace.IsGlassesAnchor)
        {
            DrawAnchor();
        }
        else
        {
            DrawHeadTracked();
        }
    }

    /// <summary>Perspective floor grid below the screens: lines to a vanishing point plus receding cross lines.</summary>
    private void DrawFloor()
    {
        double w = ActualWidth;
        double h = ActualHeight;
        double horizon = h * 0.7;
        double vx = w / 2;
        double vy = horizon - h * 0.35;
        var line = new SolidColorBrush(ColorHelper.FromArgb(18, 255, 255, 255));
        for (int i = -12; i <= 12; i++)
        {
            double bx = vx + i * w / 10;
            double t = (horizon - vy) / (h - vy); // where the ray crosses the horizon
            AddLine(vx + (bx - vx) * t, horizon, bx, h, line, 1);
        }

        for (int i = 0; i < 7; i++)
        {
            double y = horizon + (h - horizon) * Math.Pow(i / 6.0, 1.8);
            AddLine(0, y, w, y, line, 1);
        }
    }

    private void DrawHeadTracked()
    {
        double w = ActualWidth;
        double cx = w / 2;
        double cy = ActualHeight * 0.42;
        WorkspaceViewModel workspace = _workspace!;
        // Fit the whole arc into the view: degrees spanned left-right and up-down by the outermost screen edges.
        // The margin leaves room for the outer edges, which the tilt brings closer and draws larger.
        double spanH = 1;
        double spanV = 1;
        foreach (ScreenPlacement p in workspace.Placements)
        {
            spanH = Math.Max(spanH, 2 * (Math.Abs(p.YawDegrees) + WidthDegrees(p) / 2));
            spanV = Math.Max(spanV, 2 * (Math.Abs(p.PitchDegrees) + HeightDegrees(p) / 2));
        }

        double k = Math.Min(Math.Min(w * 0.84 / spanH, ActualHeight * 0.62 / spanV), w / DegreesAcross * 1.4);

        // Far screens first, so the screen in front is drawn on top.
        foreach (ScreenPlacement p in workspace.Placements.OrderByDescending(p => Math.Abs(p.YawDegrees)))
        {
            double width = WidthDegrees(p) * k * 0.94;
            double height = HeightDegrees(p) * k;
            var screen = Screen(p.ScreenId, workspace.HeadPixelSize(p.ScreenId), width, height);

            // Turned towards you with the outer edge nearer; capped so wide arcs (6 screens) stay readable.
            screen.Projection = new PlaneProjection { RotationY = -Math.Clamp(p.YawDegrees * 0.6, -MaxTilt, MaxTilt) };
            Canvas.SetLeft(screen, cx - p.YawDegrees * k - width / 2);
            Canvas.SetTop(screen, cy - p.PitchDegrees * k - height / 2);
            _canvas.Children.Add(screen);
        }

        if (_tracking is { HasPose: true })
        {
            var reticle = new Ellipse { Width = 20, Height = 20, Stroke = Accent, StrokeThickness = 2 };
            Canvas.SetLeft(reticle, cx - _tracking.Yaw * k - 10);
            Canvas.SetTop(reticle, cy - _tracking.Pitch * k - 10);
            _canvas.Children.Add(reticle);
        }
    }

    private static double WidthDegrees(ScreenPlacement p) => p.WidthMeters / p.DistanceMeters * 180 / Math.PI;

    private static double HeightDegrees(ScreenPlacement p) => Math.Atan2(p.HeightMeters, p.DistanceMeters) * 180 / Math.PI;

    private void DrawAnchor()
    {
        Resolution image = _workspace!.AnchorCanvas;
        double scale = Math.Min(ActualWidth * 0.86 / image.Width, ActualHeight * 0.62 / image.Height);
        double left = (ActualWidth - image.Width * scale) / 2;
        double top = ActualHeight * 0.42 - image.Height * scale / 2;

        var frame = new Rectangle
        {
            Width = image.Width * scale + 24,
            Height = image.Height * scale + 24,
            RadiusX = 18,
            RadiusY = 18,
            Stroke = new SolidColorBrush(ColorHelper.FromArgb(40, 255, 255, 255)),
            StrokeDashArray = [4, 4],
            StrokeThickness = 1,
        };
        Canvas.SetLeft(frame, left - 12);
        Canvas.SetTop(frame, top - 12);
        _canvas.Children.Add(frame);

        var caption = new TextBlock
        {
            Text = $"glasses image {image.Width} × {image.Height}{(_workspace.AnchorCanvasIsExample ? " (example)" : string.Empty)}",
            FontSize = 11,
            Foreground = Muted,
        };
        Canvas.SetLeft(caption, left - 12);
        Canvas.SetTop(caption, top + image.Height * scale + 18);
        _canvas.Children.Add(caption);

        int id = 1;
        foreach (PixelRect r in _workspace.AnchorRects)
        {
            var screen = Screen(id++, $"{r.Width} × {r.Height}", r.Width * scale, r.Height * scale);
            Canvas.SetLeft(screen, left + r.X * scale);
            Canvas.SetTop(screen, top + r.Y * scale);
            _canvas.Children.Add(screen);
        }
    }

    /// <summary>One glowing screen: soft halo, light face, number and resolution; the primary one gets an accent outline.</summary>
    private Grid Screen(int id, string resolution, double width, double height)
    {
        bool primary = id == _workspace!.PrimaryScreenIndex;
        var grid = new Grid { Width = Math.Max(24, width), Height = Math.Max(16, height) };
        grid.Children.Add(new Border { Margin = new Thickness(-16), CornerRadius = new CornerRadius(22), Background = new SolidColorBrush(ColorHelper.FromArgb(14, 190, 210, 245)) });
        grid.Children.Add(new Border { Margin = new Thickness(-7), CornerRadius = new CornerRadius(14), Background = new SolidColorBrush(ColorHelper.FromArgb(26, 190, 210, 245)) });
        if (primary)
        {
            grid.Children.Add(new Border { Margin = new Thickness(-5), CornerRadius = new CornerRadius(12), BorderBrush = Accent, BorderThickness = new Thickness(2) });
        }

        grid.Children.Add(new Border { CornerRadius = new CornerRadius(8), Background = Face });
        var dark = new SolidColorBrush(ColorHelper.FromArgb(255, 0x4A, 0x58, 0x72));
        grid.Children.Add(new TextBlock
        {
            Text = resolution,
            Margin = new Thickness(12, 9, 12, 0),
            FontSize = 11,
            Foreground = dark,
            Opacity = 0.8,
        });
        grid.Children.Add(new TextBlock
        {
            Text = id.ToString(CultureInfo.CurrentCulture) + (primary ? " · primary" : string.Empty),
            Margin = new Thickness(12, 0, 12, 8),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Bottom,
            FontSize = 15,
            FontWeight = Microsoft.UI.Text.FontWeights.Bold,
            Foreground = dark,
        });
        return grid;
    }

    private void AddLine(double x1, double y1, double x2, double y2, Brush brush, double thickness) =>
        _canvas.Children.Add(new Line { X1 = x1, Y1 = y1, X2 = x2, Y2 = y2, Stroke = brush, StrokeThickness = thickness });
}
