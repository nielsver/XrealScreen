using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using XrealScreen.App.Pages;

namespace XrealScreen.App;

public sealed partial class MainWindow : Window
{
    /// <summary>Narrower than this the pane cannot show icon and text, so dragging collapses it to icons.</summary>
    private const double MinOpenPaneWidth = 160;

    private const double MaxOpenPaneWidth = 480;

    /// <summary>Open width to come back to after a drag ends below <see cref="MinOpenPaneWidth"/>.</summary>
    private double _restoreWidth;

    /// <summary>Set when a drag collapsed the pane; applied once the close animation has finished.</summary>
    private double? _pendingOpenWidth;

    public MainWindow()
    {
        InitializeComponent();

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        AppWindow.TitleBar.PreferredHeightOption = TitleBarHeightOption.Tall;
        AppWindow.SetIcon("Assets/AppIcon.ico");
        AppWindow.Resize(new Windows.Graphics.SizeInt32(1200, 820));

        NavView.OpenPaneLength = Math.Clamp(App.Preferences.NavPaneWidth, MinOpenPaneWidth, MaxOpenPaneWidth);
        NavView.IsPaneOpen = App.Preferences.NavPaneOpen;
        PaneSplitter.Reference = NavView;
        UpdatePaneSplitter();
    }

    /// <summary>Applies the theme chosen on the Settings page to the whole window.</summary>
    public void SetTheme(ElementTheme theme) => RootGrid.RequestedTheme = theme;

    public ElementTheme CurrentTheme => RootGrid.RequestedTheme;

    // Reopening keeps OpenPaneLength, so the pane comes back at the width it had before it was collapsed.
    private void TitleBar_PaneToggleRequested(TitleBar sender, object args)
    {
        if (_pendingOpenWidth is { } width)
        {
            _pendingOpenWidth = null;
            NavView.OpenPaneLength = width;
        }

        NavView.IsPaneOpen = !NavView.IsPaneOpen;
        SavePaneLayout();
    }

    private void NavView_DisplayModeChanged(NavigationView sender, NavigationViewDisplayModeChangedEventArgs args) => UpdatePaneSplitter();

    private void NavView_PaneOpening(NavigationView sender, object args) => UpdatePaneSplitter();

    private void NavView_PaneClosing(NavigationView sender, NavigationViewPaneClosingEventArgs args) => UpdatePaneSplitter();

    private void NavView_PaneClosed(NavigationView sender, object args)
    {
        if (_pendingOpenWidth is { } width)
        {
            _pendingOpenWidth = null;
            NavView.OpenPaneLength = width;
        }
    }

    private void PaneSplitter_DragStarted(object? sender, EventArgs e)
    {
        // OpenPaneLength is at least MinOpenPaneWidth outside a drag, also while the pane shows icons only.
        _restoreWidth = _pendingOpenWidth ?? NavView.OpenPaneLength;
        _pendingOpenWidth = null;
    }

    // The edge follows the pointer the whole drag; whether it snaps to icons is decided on release.
    private void PaneSplitter_Dragged(object? sender, double x)
    {
        NavView.OpenPaneLength = Math.Clamp(x, NavView.CompactPaneLength, MaxOpenPaneWidth);
        NavView.IsPaneOpen = true;
        UpdatePaneSplitter();
    }

    private void PaneSplitter_DragCompleted(object? sender, EventArgs e)
    {
        if (NavView.IsPaneOpen && NavView.OpenPaneLength < MinOpenPaneWidth)
        {
            // Too narrow for icon + text: show icons only, and reopen later at the width from before this drag.
            _pendingOpenWidth = _restoreWidth;
            NavView.IsPaneOpen = false;
        }

        SavePaneLayout();
    }

    /// <summary>The splitter sits on the pane's right edge; only an inline (Expanded) pane can be resized.</summary>
    private void UpdatePaneSplitter()
    {
        bool inline = NavView.DisplayMode == NavigationViewDisplayMode.Expanded;
        PaneSplitter.Visibility = inline ? Visibility.Visible : Visibility.Collapsed;
        double edge = NavView.IsPaneOpen ? NavView.OpenPaneLength : NavView.CompactPaneLength;
        PaneSplitter.Margin = new Thickness(edge - (PaneSplitter.Width / 2), 0, 0, 0);
    }

    private void SavePaneLayout()
    {
        // Compact and Minimal modes open the pane as an overlay; that is not the user's layout choice.
        if (NavView.DisplayMode != NavigationViewDisplayMode.Expanded)
        {
            return;
        }

        App.Preferences.NavPaneWidth = _pendingOpenWidth ?? NavView.OpenPaneLength;
        App.Preferences.NavPaneOpen = NavView.IsPaneOpen;
        App.Preferences.Save();
    }

    private void TitleBar_BackRequested(TitleBar sender, object args) => NavFrame.GoBack();

    private void NavView_ItemInvoked(NavigationView sender, NavigationViewItemInvokedEventArgs args)
    {
        if (args.InvokedItemContainer?.Tag is "exit")
        {
            App.Current.ExitApp();
        }
    }

    private void NavView_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.IsSettingsSelected)
        {
            NavFrame.Navigate(typeof(SettingsPage));
            return;
        }

        if (args.SelectedItem is not NavigationViewItem item)
        {
            return;
        }

        Type page = item.Tag switch
        {
            "home" => typeof(HomePage),
            "screens" => typeof(ScreensPage),
            "tracking" => typeof(TrackingPage),
            "games" => typeof(GamesPage),
            "device" => typeof(DevicePage),
            "about" => typeof(AboutPage),
            _ => throw new InvalidOperationException($"Unknown navigation item tag: {item.Tag}"),
        };
        NavFrame.Navigate(page);
    }
}
