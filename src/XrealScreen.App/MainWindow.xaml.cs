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

    private double _dragStartWidth;
    private bool _dragStartOpen;

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
        NavView.IsPaneOpen = !NavView.IsPaneOpen;
        SavePaneLayout();
    }

    private void NavView_DisplayModeChanged(NavigationView sender, NavigationViewDisplayModeChangedEventArgs args) => UpdatePaneSplitter();

    private void NavView_PaneOpening(NavigationView sender, object args) => UpdatePaneSplitter();

    private void NavView_PaneClosing(NavigationView sender, NavigationViewPaneClosingEventArgs args) => UpdatePaneSplitter();

    private void PaneSplitter_DragStarted(object? sender, EventArgs e)
    {
        _dragStartWidth = NavView.OpenPaneLength;
        _dragStartOpen = NavView.IsPaneOpen;
    }

    private void PaneSplitter_Dragged(object? sender, double x)
    {
        if (x < MinOpenPaneWidth)
        {
            NavView.IsPaneOpen = false;
        }
        else
        {
            NavView.OpenPaneLength = Math.Min(x, MaxOpenPaneWidth);
            NavView.IsPaneOpen = true;
        }

        UpdatePaneSplitter();
    }

    private void PaneSplitter_DragCompleted(object? sender, EventArgs e)
    {
        // Dragged shut: remember the width from before the drag, not the minimum it passed on the way.
        if (_dragStartOpen && !NavView.IsPaneOpen)
        {
            NavView.OpenPaneLength = _dragStartWidth;
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

        App.Preferences.NavPaneWidth = NavView.OpenPaneLength;
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
