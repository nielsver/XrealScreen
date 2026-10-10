using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.ApplicationModel;

namespace XrealScreen.App.Pages;

public sealed partial class SettingsPage : Page
{
    private bool _loading = true;

    public SettingsPage()
    {
        InitializeComponent();
        ThemeRadio.SelectedIndex = App.MainWindow.CurrentTheme switch
        {
            ElementTheme.Light => 1,
            ElementTheme.Dark => 2,
            _ => 0,
        };
        TrayToggle.IsOn = App.Preferences.KeepRunningInTray;
        StartWorkspaceToggle.IsOn = App.Preferences.StartWorkspaceOnLaunch;
        GlassesConnectToggle.IsOn = App.Preferences.StartWorkspaceWhenGlassesConnect;
        LocalApiToggle.IsOn = App.Preferences.EnableLocalApi;
        LoadAutoStartWorkspaces();
        Loaded += async (_, _) =>
        {
            await LoadStartupStateAsync().ConfigureAwait(true);
            _loading = false;
        };
    }

    private async Task LoadStartupStateAsync()
    {
        var task = await StartupTask.GetAsync(App.StartupTaskId);
        StartWithWindowsToggle.IsOn = task.State is StartupTaskState.Enabled or StartupTaskState.EnabledByPolicy;
        ShowStartupHint(task.State);
    }

    private async void StartWithWindows_Toggled(object sender, RoutedEventArgs e)
    {
        if (_loading)
        {
            return;
        }

        try
        {
            var task = await StartupTask.GetAsync(App.StartupTaskId);
            var state = StartWithWindowsToggle.IsOn ? await task.RequestEnableAsync() : Disable(task);
            _loading = true;
            StartWithWindowsToggle.IsOn = state is StartupTaskState.Enabled or StartupTaskState.EnabledByPolicy;
            _loading = false;
            ShowStartupHint(state);
        }
        catch (Exception ex)
        {
            CrashLog.Write("StartupTask", ex);
            StartupHint.Text = $"Could not change the startup setting: {ex.Message}";
            StartupHint.Visibility = Visibility.Visible;
        }
    }

    private static StartupTaskState Disable(StartupTask task)
    {
        task.Disable();
        return task.State;
    }

    private void ShowStartupHint(StartupTaskState state)
    {
        string? hint = state switch
        {
            StartupTaskState.DisabledByUser => "Windows blocks this because start-up was turned off for XrealScreen in Task Manager → Startup apps. Turn it on there.",
            StartupTaskState.DisabledByPolicy => "Start-up is disabled by a system policy.",
            _ => null,
        };
        StartupHint.Text = hint ?? string.Empty;
        StartupHint.Visibility = hint is null ? Visibility.Collapsed : Visibility.Visible;
    }

    private async void LocalApi_Toggled(object sender, RoutedEventArgs e)
    {
        if (App.Preferences.EnableLocalApi == LocalApiToggle.IsOn)
        {
            return;
        }

        App.Preferences.EnableLocalApi = LocalApiToggle.IsOn;
        App.Preferences.Save();
        await App.Current.SetLocalApiEnabledAsync(LocalApiToggle.IsOn).ConfigureAwait(true);
    }

    private void Tray_Toggled(object sender, RoutedEventArgs e)
    {
        App.Preferences.KeepRunningInTray = TrayToggle.IsOn;
        App.Preferences.Save();
    }

    private void StartWorkspace_Toggled(object sender, RoutedEventArgs e)
    {
        App.Preferences.StartWorkspaceOnLaunch = StartWorkspaceToggle.IsOn;
        App.Preferences.Save();
    }

    private const string LatestSettings = "Latest settings (what you used last)";

    private void LoadAutoStartWorkspaces()
    {
        List<string> items = [LatestSettings, .. App.Session.SavedWorkspaces];
        string? chosen = App.Preferences.AutoStartWorkspaceName;
        if (chosen is not null && !items.Contains(chosen))
        {
            items.Add(chosen); // deleted meanwhile: keep showing it; the start falls back to the latest settings
        }

        AutoStartWorkspaceBox.ItemsSource = items;
        AutoStartWorkspaceBox.SelectedItem = chosen ?? LatestSettings;
    }

    private void AutoStartWorkspace_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (AutoStartWorkspaceBox.SelectedItem is not string item)
        {
            return;
        }

        string? name = item == LatestSettings ? null : item;
        if (App.Preferences.AutoStartWorkspaceName != name)
        {
            App.Preferences.AutoStartWorkspaceName = name;
            App.Preferences.Save();
        }
    }

    private void GlassesConnect_Toggled(object sender, RoutedEventArgs e)
    {
        App.Preferences.StartWorkspaceWhenGlassesConnect = GlassesConnectToggle.IsOn;
        App.Preferences.Save();
    }

    private void ThemeRadio_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        int index = Math.Max(0, ThemeRadio.SelectedIndex);
        App.MainWindow.SetTheme(App.ThemeFromIndex(index));
        if (App.Preferences.Theme != index)
        {
            App.Preferences.Theme = index;
            App.Preferences.Save();
        }
    }
}
