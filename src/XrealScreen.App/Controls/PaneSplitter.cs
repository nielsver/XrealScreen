using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;

namespace XrealScreen.App.Controls;

/// <summary>
/// Thin vertical drag handle with the left-right resize cursor. Reports the pointer's x position
/// relative to <see cref="Reference"/> while it is dragged; the owner decides what to resize.
/// </summary>
public sealed partial class PaneSplitter : Grid
{
    private readonly Microsoft.UI.Xaml.Shapes.Rectangle _line = new()
    {
        Width = 2,
        HorizontalAlignment = HorizontalAlignment.Center,
        Opacity = 0,
    };

    private bool _dragging;

    public PaneSplitter()
    {
        Width = 8;
        Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent); // hit-testable
        ProtectedCursor = InputSystemCursor.Create(InputSystemCursorShape.SizeWestEast);
        Children.Add(_line);
        Loaded += (_, _) => _line.Fill = (Brush)Application.Current.Resources["AccentFillColorDefaultBrush"];
        PointerEntered += OnPointerEntered;
        PointerExited += OnPointerExited;
        PointerPressed += OnPointerPressed;
        PointerMoved += OnPointerMoved;
        PointerReleased += OnPointerReleased;
        PointerCaptureLost += (_, _) => EndDrag();
    }

    /// <summary>Element the reported x positions are relative to.</summary>
    public UIElement? Reference { get; set; }

    public event EventHandler? DragStarted;

    public event EventHandler<double>? Dragged;

    public event EventHandler? DragCompleted;

    private void OnPointerEntered(object sender, PointerRoutedEventArgs e) => _line.Opacity = 1;

    private void OnPointerExited(object sender, PointerRoutedEventArgs e)
    {
        if (!_dragging)
        {
            _line.Opacity = 0;
        }
    }

    private void OnPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (CapturePointer(e.Pointer))
        {
            _dragging = true;
            e.Handled = true;
            DragStarted?.Invoke(this, EventArgs.Empty);
        }
    }

    private void OnPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (_dragging)
        {
            Dragged?.Invoke(this, e.GetCurrentPoint(Reference).Position.X);
            e.Handled = true;
        }
    }

    private void OnPointerReleased(object sender, PointerRoutedEventArgs e)
    {
        ReleasePointerCapture(e.Pointer);
        EndDrag();
    }

    private void EndDrag()
    {
        if (!_dragging)
        {
            return;
        }

        _dragging = false;
        _line.Opacity = 0;
        DragCompleted?.Invoke(this, EventArgs.Empty);
    }
}
