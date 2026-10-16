using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using XrealScreen.App.Looks;
using XrealScreen.App.ViewModels;

namespace XrealScreen.App.Pages.Looks;

/// <summary>Home page of the Stage look.</summary>
public sealed partial class StageHomePage : Page
{
    public StageHomePage() => InitializeComponent();

    public WorkspaceViewModel Workspace => App.Workspace;

    public TrackingViewModel Tracking => App.Tracking;

    public DeviceViewModel Device => App.Device;

    public SessionViewModel Session => App.Session;

    private void StartStop_Click(object sender, RoutedEventArgs e) => LookFormat.StartOrStop();

    private void Kind_Click(object sender, RoutedEventArgs e)
    {
        var toggle = (ToggleButton)sender;
        Workspace.KindIndex = int.Parse((string)toggle.Tag, System.Globalization.CultureInfo.InvariantCulture);
        toggle.IsChecked = true; // clicking the selected segment keeps it selected
    }

    private void ScreenCount_Click(object sender, RoutedEventArgs e)
    {
        var toggle = (ToggleButton)sender;
        Workspace.ScreenCount = int.Parse((string)toggle.Tag, System.Globalization.CultureInfo.InvariantCulture);
        toggle.IsChecked = true;
    }

    private void Fewer_Click(object sender, RoutedEventArgs e) => LookFormat.StepScreens(-1);

    private void More_Click(object sender, RoutedEventArgs e) => LookFormat.StepScreens(1);

    /// <summary>Lists the saved workspaces; picking one loads it (restarting a running workspace).</summary>
    private void WorkspaceMenu_Opening(object sender, object e)
    {
        WorkspaceMenu.Items.Clear();
        if (Session.SavedWorkspaces.Count == 0)
        {
            WorkspaceMenu.Items.Add(new MenuFlyoutItem { Text = "No saved workspaces yet (… in the dock → Save)", IsEnabled = false });
            return;
        }

        foreach (string name in Session.SavedWorkspaces)
        {
            var item = new MenuFlyoutItem { Text = name };
            item.Click += (_, _) =>
            {
                Session.SelectedWorkspace = name;
                if (Session.LoadWorkspaceCommand.CanExecute(null))
                {
                    Session.LoadWorkspaceCommand.Execute(null);
                }
            };
            WorkspaceMenu.Items.Add(item);
        }
    }

    private async void Import_Click(object sender, RoutedEventArgs e) => await WorkspaceFileDialogs.ImportAsync().ConfigureAwait(true);

    private async void Export_Click(object sender, RoutedEventArgs e) => await WorkspaceFileDialogs.ExportAsync().ConfigureAwait(true);
}
