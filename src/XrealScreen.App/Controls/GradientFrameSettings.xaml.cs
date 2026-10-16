using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using XrealScreen.App.ViewModels;

namespace XrealScreen.App.Controls;

/// <summary>Pictured frame looks, per-screen colors, and brightness and dimmer sent to the glasses.</summary>
public sealed partial class GradientFrameSettings : UserControl
{
    public GradientFrameSettings() => InitializeComponent();

    public WorkspaceViewModel Workspace => App.Workspace;

    private void Look_Chosen(object sender, RoutedEventArgs e)
    {
        if (sender is FrameLookButton button)
        {
            Workspace.ApplyLook(button.LookName);
        }
    }
}
