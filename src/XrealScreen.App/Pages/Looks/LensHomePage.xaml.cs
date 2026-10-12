using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using XrealScreen.App.Looks;
using XrealScreen.App.ViewModels;

namespace XrealScreen.App.Pages.Looks;

/// <summary>Home page of the Lens look.</summary>
public sealed partial class LensHomePage : Page
{
    public LensHomePage() => InitializeComponent();

    public WorkspaceViewModel Workspace => App.Workspace;

    public TrackingViewModel Tracking => App.Tracking;

    public DeviceViewModel Device => App.Device;

    public SessionViewModel Session => App.Session;

    private void StartStop_Click(object sender, RoutedEventArgs e) => LookFormat.StartOrStop();

    private async void Import_Click(object sender, RoutedEventArgs e) => await WorkspaceFileDialogs.ImportAsync().ConfigureAwait(true);

    private async void Export_Click(object sender, RoutedEventArgs e) => await WorkspaceFileDialogs.ExportAsync().ConfigureAwait(true);
}
