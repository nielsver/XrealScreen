using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using XrealScreen.App.Looks;
using XrealScreen.App.Pages;
using XrealScreen.App.Pages.Looks;

namespace XrealScreen.App;

public sealed partial class MainWindow : Window
{
    /// <summary>Smallest saved open width; the real snap point is where the labels end (<see cref="_labelsEnd"/>).</summary>
    private const double MinOpenPaneWidth = 120;

    private const double MaxOpenPaneWidth = 480;

    /// <summary>Opacity of the labels while a drag is narrower than they are: releasing there shows icons only.</summary>
    private const double CutLabelOpacity = 0.3;

    /// <summary>Open width to come back to after a drag ends with the labels cut.</summary>
    private double _restoreWidth;

    /// <summary>Pane width that fits the longest label; measured on the first drag with the pane open.</summary>
    private double? _labelsEnd;

    private readonly List<TextBlock> _labels = [];

    /// <summary>Set when a drag collapsed the pane; applied once the close animation has finished.</summary>
    private double? _pendingOpenWidth;

    /// <summary>Theme chosen on the Settings page; Lens, Instrument and Stage override it (see <see cref="EffectiveTheme"/>).</summary>
    private ElementTheme _userTheme;

    /// <summary>The merged look dictionary (Looks/*.xaml) currently in Application.Resources.</summary>
    private ResourceDictionary? _lookResources;

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

    /// <summary>Applies the theme chosen on the Settings page to the whole window (Classic look; the others have their own).</summary>
    public void SetTheme(ElementTheme theme)
    {
        _userTheme = theme;
        RootGrid.RequestedTheme = EffectiveTheme;
    }

    public ElementTheme CurrentTheme => _userTheme;

    public AppLook Look { get; private set; }

    /// <summary>Lens and Stage are designed dark, Instrument light; Classic follows the theme setting.</summary>
    private ElementTheme EffectiveTheme => Look switch
    {
        AppLook.Lens or AppLook.Stage => ElementTheme.Dark,
        AppLook.Instrument => ElementTheme.Light,
        _ => _userTheme,
    };

    /// <summary>
    /// Switches the window look while the app runs: swaps the look dictionary in Application.Resources,
    /// re-resolves theme resources in the shell and rebuilds the open page with the new resources.
    /// </summary>
    public void ApplyLook(AppLook look)
    {
        Look = look;
        var merged = Application.Current.Resources.MergedDictionaries;
        var next = new ResourceDictionary { Source = new Uri($"ms-appx:///Looks/{look}Look.xaml") };
        int index = _lookResources is null
            ? merged.ToList().FindIndex(d => d.Source?.AbsolutePath.StartsWith("/Looks/", StringComparison.OrdinalIgnoreCase) == true)
            : merged.IndexOf(_lookResources);
        if (index >= 0)
        {
            merged[index] = next;
        }
        else
        {
            merged.Add(next);
        }

        _lookResources = next;
        RootGrid.Background = (Brush)next["LookShellBackground"];

        // Stage keeps the pane as an icon rail; the others use the resizable pane.
        bool rail = look == AppLook.Stage;
        NavView.PaneDisplayMode = rail ? NavigationViewPaneDisplayMode.LeftCompact : NavigationViewPaneDisplayMode.Auto;
        NavView.IsPaneOpen = !rail && App.Preferences.NavPaneOpen;

        // Theme resources are re-read only on a theme change: flip the theme once to pick up the new brushes.
        ElementTheme theme = EffectiveTheme;
        RootGrid.RequestedTheme = theme == ElementTheme.Dark ? ElementTheme.Light : ElementTheme.Dark;
        RootGrid.RequestedTheme = theme;
        UpdatePaneSplitter();

        if (NavView.SelectedItem is { } selected && NavFrame.Content is not null)
        {
            NavigateTo(selected);
            if (NavFrame.BackStack.Count > 0)
            {
                NavFrame.BackStack.RemoveAt(NavFrame.BackStack.Count - 1); // a rebuilt page is not a step back
            }
        }
    }

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
        // OpenPaneLength is the open width outside a drag, also while the pane shows icons only.
        _restoreWidth = _pendingOpenWidth ?? NavView.OpenPaneLength;
        _pendingOpenWidth = null;
    }

    // The edge follows the pointer the whole drag; whether it snaps to icons is decided on release.
    // Left of the labels' end they fade, so you can see where releasing switches to icons only.
    private void PaneSplitter_Dragged(object? sender, double x)
    {
        NavView.OpenPaneLength = Math.Clamp(x, NavView.CompactPaneLength, MaxOpenPaneWidth);
        NavView.IsPaneOpen = true;
        UpdatePaneSplitter();

        _labelsEnd ??= MeasureLabelsEnd();
        double opacity = IsLabelCut() ? CutLabelOpacity : 1;
        foreach (TextBlock label in _labels)
        {
            label.Opacity = opacity;
        }
    }

    private void PaneSplitter_DragCompleted(object? sender, EventArgs e)
    {
        foreach (TextBlock label in _labels)
        {
            label.Opacity = 1;
        }

        if (NavView.IsPaneOpen && IsLabelCut())
        {
            // Too narrow for icon + text: show icons only, and reopen later at the width from before this drag.
            _pendingOpenWidth = Math.Max(_restoreWidth, _labelsEnd ?? MinOpenPaneWidth);
            NavView.IsPaneOpen = false;
        }

        SavePaneLayout();
    }

    private bool IsLabelCut() => NavView.OpenPaneLength < (_labelsEnd ?? MinOpenPaneWidth);

    /// <summary>
    /// Right edge (relative to the pane) of the longest navigation label at its full length, or null while
    /// the labels are not laid out yet. Also collects the labels' text blocks for fading.
    /// </summary>
    private double? MeasureLabelsEnd()
    {
        _labels.Clear();
        double end = 0;
        IEnumerable<NavigationViewItem> items = NavView.MenuItems.Concat(NavView.FooterMenuItems)
            .Append(NavView.SettingsItem)
            .OfType<NavigationViewItem>();
        foreach (NavigationViewItem item in items)
        {
            if (item.Content is not string text)
            {
                continue;
            }

            foreach (TextBlock label in Descendants(item).OfType<TextBlock>().Where(t => t.Text == text && t.ActualHeight > 0))
            {
                var probe = new TextBlock { Text = text, FontFamily = label.FontFamily, FontSize = label.FontSize, FontWeight = label.FontWeight };
                probe.Measure(new Windows.Foundation.Size(double.PositiveInfinity, double.PositiveInfinity));
                double left = label.TransformToVisual(NavView).TransformPoint(default).X;
                end = Math.Max(end, left + probe.DesiredSize.Width);
                _labels.Add(label);
            }
        }

        // A little room after the text, like the item's own padding.
        return end > 0 ? Math.Ceiling(end) + 8 : null;
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
    {
        int count = VisualTreeHelper.GetChildrenCount(parent);
        for (int i = 0; i < count; i++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(parent, i);
            yield return child;
            foreach (DependencyObject descendant in Descendants(child))
            {
                yield return descendant;
            }
        }
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
        if (args.SelectedItem is { } selected)
        {
            NavigateTo(selected);
        }
    }

    private void NavigateTo(object selected)
    {
        if (ReferenceEquals(selected, NavView.SettingsItem))
        {
            NavFrame.Navigate(typeof(SettingsPage));
            return;
        }

        if (selected is not NavigationViewItem item)
        {
            return;
        }

        Type page = item.Tag switch
        {
            "home" => Look switch
            {
                AppLook.Lens => typeof(LensHomePage),
                AppLook.Instrument => typeof(InstrumentHomePage),
                AppLook.Stage => typeof(StageHomePage),
                _ => typeof(HomePage),
            },
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
