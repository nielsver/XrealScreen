using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace XrealScreen.App.Controls;

/// <summary>One tappable picture of a frame look. The ring shows the look that is on.</summary>
public sealed partial class FrameLookButton : UserControl
{
    public static readonly DependencyProperty SwatchProperty = DependencyProperty.Register(
        nameof(Swatch), typeof(Brush), typeof(FrameLookButton), new PropertyMetadata(null, OnVisual));

    public static readonly DependencyProperty CaptionProperty = DependencyProperty.Register(
        nameof(Caption), typeof(string), typeof(FrameLookButton), new PropertyMetadata(string.Empty, OnVisual));

    public static readonly DependencyProperty SelectedProperty = DependencyProperty.Register(
        nameof(Selected), typeof(bool), typeof(FrameLookButton), new PropertyMetadata(false, OnVisual));

    public static readonly DependencyProperty LookNameProperty = DependencyProperty.Register(
        nameof(LookName), typeof(string), typeof(FrameLookButton), new PropertyMetadata(string.Empty, OnVisual));

    public FrameLookButton()
    {
        InitializeComponent();
        Loaded += (_, _) => ApplyVisual();
    }

    public Brush? Swatch
    {
        get => (Brush?)GetValue(SwatchProperty);
        set => SetValue(SwatchProperty, value);
    }

    public string Caption
    {
        get => (string)GetValue(CaptionProperty);
        set => SetValue(CaptionProperty, value);
    }

    public bool Selected
    {
        get => (bool)GetValue(SelectedProperty);
        set => SetValue(SelectedProperty, value);
    }

    public string LookName
    {
        get => (string)GetValue(LookNameProperty);
        set => SetValue(LookNameProperty, value);
    }

    public event RoutedEventHandler? LookChosen;

    private static void OnVisual(DependencyObject sender, DependencyPropertyChangedEventArgs e) =>
        ((FrameLookButton)sender).ApplyVisual();

    private void ApplyVisual()
    {
        if (SwatchHost is null || Ring is null || CaptionText is null || RootButton is null)
        {
            return;
        }

        SwatchHost.Background = Swatch;
        CaptionText.Text = Caption;
        CaptionText.FontWeight = Selected ? FontWeights.SemiBold : FontWeights.Normal;
        AutomationProperties.SetName(RootButton, string.IsNullOrEmpty(LookName) ? Caption : LookName + " look");
        Ring.BorderThickness = new Thickness(Selected ? 3 : 1);
        Ring.BorderBrush = Selected ? AccentBrush() : new SolidColorBrush(Color.FromArgb(48, 255, 255, 255));
    }

    private static Brush AccentBrush()
    {
        if (Application.Current?.Resources.TryGetValue("AccentFillColorDefaultBrush", out object? brush) == true && brush is Brush accent)
        {
            return accent;
        }

        return new SolidColorBrush(Color.FromArgb(255, 0x60, 0xCD, 0xFF));
    }

    private void Root_Click(object sender, RoutedEventArgs e) => LookChosen?.Invoke(this, e);
}
