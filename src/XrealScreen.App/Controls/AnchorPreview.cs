using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.Foundation;
using Windows.UI;
using XrealScreen.App.ViewModels;
using XrealScreen.Core.Workspace;
using XrealScreen.Host;

namespace XrealScreen.App.Controls;

/// <summary>
/// Picture of the glasses-anchor workspace: the glasses image (dark frame, to scale) with the
/// virtual screens drawn where they will appear, labelled with number and resolution.
/// </summary>
public sealed partial class AnchorPreview : UserControl
{
    private readonly Canvas _canvas = new();
    private WorkspaceViewModel? _workspace;

    public AnchorPreview()
    {
        Content = _canvas;
        SizeChanged += (_, _) => Redraw();
        Loaded += (_, _) =>
        {
            _workspace = App.Workspace;
            _workspace.AnchorPreviewChanged += OnChanged;
            _workspace.RefreshGlassesSignal();
        };
        Unloaded += (_, _) =>
        {
            if (_workspace is not null)
            {
                _workspace.AnchorPreviewChanged -= OnChanged;
            }
        };
        MinHeight = 120;
    }

    private void OnChanged(object? sender, EventArgs e) => Redraw();

    private void Redraw()
    {
        _canvas.Children.Clear();
        if (_workspace is null || ActualWidth <= 0 || ActualHeight <= 0)
        {
            return;
        }

        var glasses = _workspace.AnchorCanvas;
        double scale = Math.Min(ActualWidth / glasses.Width, (ActualHeight - 24) / glasses.Height);
        double frameW = glasses.Width * scale, frameH = glasses.Height * scale;
        double ox = (ActualWidth - frameW) / 2, oy = 0;

        var res = Application.Current.Resources;
        var frame = new Rectangle
        {
            Width = frameW,
            Height = frameH,
            RadiusX = 10,
            RadiusY = 10,
            Fill = new SolidColorBrush(Microsoft.UI.Colors.Black),
            Stroke = (Brush)res["ControlStrokeColorDefaultBrush"],
            StrokeThickness = 1,
        };
        Canvas.SetLeft(frame, ox);
        Canvas.SetTop(frame, oy);
        _canvas.Children.Add(frame);
        if (_workspace.FrameStyle != FrameStyle.SeeThrough)
        {
            DrawColorBands(ox, oy, frameW, frameH);
        }

        int n = 1;
        foreach (var r in _workspace.AnchorRects)
        {
            double corner = Math.Max(6, 22 * scale);
            if (_workspace.FrameStyle != FrameStyle.SeeThrough)
            {
                var shadow = new Rectangle
                {
                    Width = r.Width * scale + 10,
                    Height = r.Height * scale + 12,
                    RadiusX = corner + 2,
                    RadiusY = corner + 2,
                    Fill = new SolidColorBrush(Color.FromArgb(110, 0, 0, 0)),
                };
                Canvas.SetLeft(shadow, ox + r.X * scale - 3);
                Canvas.SetTop(shadow, oy + r.Y * scale + 5);
                _canvas.Children.Add(shadow);
            }

            var screen = new Rectangle
            {
                Width = r.Width * scale,
                Height = r.Height * scale,
                RadiusX = corner,
                RadiusY = corner,
                Fill = (Brush)res["AccentFillColorSecondaryBrush"],
                Stroke = new SolidColorBrush(Color.FromArgb(230, 214, 228, 255)),
                StrokeThickness = 2,
            };
            Canvas.SetLeft(screen, ox + r.X * scale);
            Canvas.SetTop(screen, oy + r.Y * scale);
            _canvas.Children.Add(screen);

            var label = new TextBlock
            {
                Text = n == _workspace.PrimaryScreenIndex ? $"{n++} · primary\n{r.Width}×{r.Height}" : $"{n++}\n{r.Width}×{r.Height}",
                Foreground = (Brush)res["TextOnAccentFillColorPrimaryBrush"],
                FontSize = 12,
                TextAlignment = TextAlignment.Center,
            };
            label.Measure(new Windows.Foundation.Size(double.PositiveInfinity, double.PositiveInfinity));
            Canvas.SetLeft(label, ox + (r.X + r.Width / 2.0) * scale - label.DesiredSize.Width / 2);
            Canvas.SetTop(label, oy + (r.Y + r.Height / 2.0) * scale - label.DesiredSize.Height / 2);
            _canvas.Children.Add(label);
        }

        var caption = new TextBlock
        {
            Text = _workspace.AnchorCanvasIsExample
                ? $"Example: glasses in UltraWide 32:9 ({glasses})"
                : $"Glasses image {glasses} — held still by the glasses (Anchor mode)",
            Foreground = (Brush)res["TextFillColorSecondaryBrush"],
            FontSize = 12,
        };
        Canvas.SetLeft(caption, ox);
        Canvas.SetTop(caption, oy + frameH + 4);
        _canvas.Children.Add(caption);
    }

    private void DrawColorBands(double ox, double oy, double frameW, double frameH)
    {
        var canvas = _workspace!.AnchorCanvas;
        var rects = _workspace.AnchorRects;
        int count = Math.Clamp(rects.Count == 0 ? _workspace.ScreenCountIndex + 1 : rects.Count, 1, 3);
        bool stacked = canvas.Height > canvas.Width;
        float split1 = count >= 2 ? 1f / count : 1f;
        float split2 = count >= 3 ? 2f / count : 1f;
        if (rects.Count >= 2)
        {
            (split1, split2, stacked) = AnchorSplit.Bands(canvas.Width, canvas.Height, rects);
        }

        float[] edges = [0f, count >= 2 ? split1 : 1f, count >= 3 ? split2 : 1f, 1f];
        Color[] first = [_workspace.Screen1ColorA, _workspace.Screen2ColorA, _workspace.Screen3ColorA];
        Color[] second = [_workspace.Screen1ColorB, _workspace.Screen2ColorB, _workspace.Screen3ColorB];
        for (int i = 0; i < count; i++)
        {
            double start = edges[i];
            double end = edges[i + 1];
            var band = new Rectangle
            {
                Width = stacked ? frameW : Math.Max(1, (end - start) * frameW),
                Height = stacked ? Math.Max(1, (end - start) * frameH) : frameH,
                Fill = BandBrush(_workspace.FrameStyle, first[i], second[i], stacked),
            };
            Canvas.SetLeft(band, stacked ? ox : ox + start * frameW);
            Canvas.SetTop(band, stacked ? oy + start * frameH : oy);
            _canvas.Children.Add(band);
        }
    }

    private static Brush BandBrush(FrameStyle style, Color first, Color second, bool stacked)
    {
        return style switch
        {
            FrameStyle.Solid => new SolidColorBrush(first),
            FrameStyle.Vignette => Radial(second, first),
            FrameStyle.Glow => Radial(Color.FromArgb(255, 0, 0, 0), first),
            FrameStyle.Vertical => Linear(first, second, stacked ? new Point(1, 0) : new Point(0, 1)),
            _ => Linear(first, second, stacked ? new Point(0, 1) : new Point(1, 0)),
        };
    }

    private static LinearGradientBrush Linear(Color first, Color second, Point end)
    {
        var brush = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = end };
        brush.GradientStops.Add(new GradientStop { Color = first, Offset = 0 });
        brush.GradientStops.Add(new GradientStop { Color = second, Offset = 1 });
        return brush;
    }

    private static RadialGradientBrush Radial(Color center, Color edge)
    {
        var brush = new RadialGradientBrush { Center = new Point(0.5, 0.5), GradientOrigin = new Point(0.5, 0.5), RadiusX = 0.75, RadiusY = 0.75 };
        brush.GradientStops.Add(new GradientStop { Color = center, Offset = 0 });
        brush.GradientStops.Add(new GradientStop { Color = edge, Offset = 1 });
        return brush;
    }
}
