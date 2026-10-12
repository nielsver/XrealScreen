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
/// Instrument look: the workspace on a yaw scale (−90° … +90°, ruled every 15°) with the head direction as a line.
/// Head tracking places screens by their yaw; glasses anchor spreads them across the glasses' field of view.
/// </summary>
public sealed partial class FieldMap : UserControl
{
    private const double YawRange = 180;
    private const double PitchRange = 70;
    private const double FovH = 46; // same as WorkspacePreview
    private const double FovV = 26;
    private const double ScaleHeight = 20;

    private readonly Canvas _canvas = new();
    private WorkspaceViewModel? _workspace;
    private TrackingViewModel? _tracking;

    public FieldMap()
    {
        Content = _canvas;
        SizeChanged += (_, _) => Redraw();
        Loaded += (_, _) => Attach();
        Unloaded += (_, _) => Detach();
        MinHeight = 160;
    }

    public Brush Ink { get; set; } = new SolidColorBrush(Colors.Black);

    public Brush Muted { get; set; } = new SolidColorBrush(Colors.Gray);

    public Brush Rule { get; set; } = new SolidColorBrush(Colors.LightGray);

    public Brush Field { get; set; } = new SolidColorBrush(Colors.White);

    public Brush Screen { get; set; } = new SolidColorBrush(Colors.SteelBlue);

    public Brush Signal { get; set; } = new SolidColorBrush(Colors.OrangeRed);

    public FontFamily Mono { get; set; } = new("Consolas");

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
        if (_workspace is null || ActualWidth <= 0 || ActualHeight <= ScaleHeight)
        {
            return;
        }

        double w = ActualWidth;
        double h = ActualHeight - ScaleHeight;
        double sx = w / YawRange;
        double sy = h / PitchRange;
        double cx = w / 2;
        double cy = h / 2;

        var field = new Rectangle { Width = w, Height = h, Fill = Field, Stroke = Rule, StrokeThickness = 1 };
        _canvas.Children.Add(field);

        // Rulings every 15° across, every 10° down; labels under the field.
        for (int deg = -90; deg <= 90; deg += 15)
        {
            double x = cx - deg * sx;
            AddLine(x, 0, x, h, deg == 0 ? Muted : Rule, deg == 0 ? 1 : 0.6);
            string text = FormatYaw(deg).TrimEnd('°').Replace(".0", string.Empty, StringComparison.Ordinal);
            var label = new TextBlock { Text = text, FontFamily = Mono, FontSize = 10, Foreground = Muted };
            label.Measure(new Windows.Foundation.Size(double.PositiveInfinity, double.PositiveInfinity));
            Canvas.SetLeft(label, Math.Clamp(x - label.DesiredSize.Width / 2, 0, w - label.DesiredSize.Width));
            Canvas.SetTop(label, h + 4);
            _canvas.Children.Add(label);
        }

        for (int deg = -30; deg <= 30; deg += 10)
        {
            double y = cy - deg * sy;
            AddLine(0, y, w, y, Rule, 0.5);
        }

        foreach (var (id, left, top, width, height, yaw) in Screens(sx, sy, cx, cy))
        {
            bool primary = id == _workspace.PrimaryScreenIndex;
            var block = new Border
            {
                Width = Math.Max(8, width),
                Height = Math.Max(8, height),
                BorderBrush = Screen,
                BorderThickness = new Thickness(primary ? 2.5 : 1.5),
                Background = new SolidColorBrush(ColorHelper.FromArgb(22, 31, 79, 143)),
                Child = new TextBlock
                {
                    Text = $"S{id}{(primary ? " · PRIMARY" : string.Empty)}\n{FormatYaw(yaw)}",
                    FontFamily = Mono,
                    FontSize = 11,
                    FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                    Foreground = Screen,
                    TextAlignment = TextAlignment.Center,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                },
            };
            Canvas.SetLeft(block, left);
            Canvas.SetTop(block, top);
            _canvas.Children.Add(block);
        }

        if (_tracking is { HasPose: true })
        {
            double x = cx - _tracking.Yaw * sx;
            AddLine(x, -4, x, h + 2, Signal, 2);
            var tag = new TextBlock
            {
                Text = "HEAD " + FormatYaw(_tracking.Yaw),
                FontFamily = Mono,
                FontSize = 10,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = Signal,
            };
            Canvas.SetLeft(tag, Math.Min(x + 5, w - 80));
            Canvas.SetTop(tag, 4);
            _canvas.Children.Add(tag);
        }
    }

    private IEnumerable<(int Id, double Left, double Top, double Width, double Height, double Yaw)> Screens(double sx, double sy, double cx, double cy)
    {
        if (_workspace!.IsGlassesAnchor)
        {
            // One glasses image: the screens sit inside the field of view straight ahead.
            var canvas = _workspace.AnchorCanvas;
            double fx = FovH * sx / canvas.Width;
            double fy = FovV * sy / canvas.Height;
            double x0 = cx - FovH * sx / 2;
            double y0 = cy - FovV * sy / 2;
            int id = 1;
            foreach (PixelRect r in _workspace.AnchorRects)
            {
                double centerDeg = ((r.X + r.Width / 2.0) / canvas.Width - 0.5) * FovH;
                yield return (id++, x0 + r.X * fx, y0 + r.Y * fy, r.Width * fx, r.Height * fy, -centerDeg);
            }

            yield break;
        }

        foreach (ScreenPlacement p in _workspace.Placements)
        {
            double wDeg = p.WidthMeters / p.DistanceMeters * 180 / Math.PI;
            double hDeg = Math.Atan2(p.HeightMeters, p.DistanceMeters) * 180 / Math.PI;
            double width = wDeg * sx;
            double height = hDeg * sy;
            yield return (p.ScreenId, cx - p.YawDegrees * sx - width / 2, cy - p.PitchDegrees * sy - height / 2, width, height, p.YawDegrees);
        }
    }

    // Positive yaw = left; the scale reads like a map, left negative.
    private static string FormatYaw(double yaw)
    {
        double v = -yaw;
        return (v > 0.05 ? "+" : v < -0.05 ? "−" : string.Empty) + Math.Abs(v).ToString("0.0", CultureInfo.InvariantCulture) + "°";
    }

    private void AddLine(double x1, double y1, double x2, double y2, Brush brush, double thickness) =>
        _canvas.Children.Add(new Line { X1 = x1, Y1 = y1, X2 = x2, Y2 = y2, Stroke = brush, StrokeThickness = thickness });
}
