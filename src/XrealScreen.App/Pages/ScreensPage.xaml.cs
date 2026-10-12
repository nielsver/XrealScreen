using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using XrealScreen.App.Controls;
using XrealScreen.App.Looks;
using XrealScreen.App.ViewModels;

namespace XrealScreen.App.Pages;

public sealed partial class ScreensPage : Page
{
    public ScreensPage()
    {
        InitializeComponent();
        Workspace.RefreshGlassesSignal();
        if (App.MainWindow.Look == AppLook.Stage)
        {
            UseStageView(TrackingPreview, TrackingStageHost, 340);
            UseStageView(AnchorLayoutPreview, AnchorStageHost, 300);
        }
    }

    /// <summary>Stage look: the same monitors-in-a-dark-room view as its Home page instead of the flat preview.</summary>
    private static void UseStageView(FrameworkElement preview, Grid host, double height)
    {
        var resources = Application.Current.Resources;
        var stage = new StageView
        {
            Accent = (Brush)resources["StageAmberBrush"],
            Face = (Brush)resources["StageScreenFaceBrush"],
            Muted = (Brush)resources["StageMutedBrush"],
            IsHitTestVisible = false,
        };
        var room = new Grid
        {
            Height = height,
            CornerRadius = new CornerRadius(10),
            Background = (Brush)resources["StageRoomBrush"],
            Children =
            {
                new Microsoft.UI.Xaml.Shapes.Ellipse
                {
                    Width = 900,
                    Height = 420,
                    Fill = (Brush)resources["StageGlowBrush"],
                    IsHitTestVisible = false,
                },
                stage,
            },
        };

        preview.Visibility = Visibility.Collapsed;
        host.Children.Add(room);
        host.Visibility = Visibility.Visible;
    }

    public WorkspaceViewModel Workspace => App.Workspace;

    public SessionViewModel Session => App.Session;

    private void RefreshSignal_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e) => Workspace.RefreshGlassesSignal();
}
