using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using XrealScreen.App.Pages;

namespace XrealScreen.App;

public sealed partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        AppWindow.TitleBar.PreferredHeightOption = TitleBarHeightOption.Tall;
        AppWindow.SetIcon("Assets/AppIcon.ico");
        AppWindow.Resize(new Windows.Graphics.SizeInt32(1200, 820));
    }

    /// <summary>Applies the theme chosen on the Settings page to the whole window.</summary>
    public void SetTheme(ElementTheme theme) => RootGrid.RequestedTheme = theme;

    public ElementTheme CurrentTheme => RootGrid.RequestedTheme;

    private void TitleBar_PaneToggleRequested(TitleBar sender, object args) => NavView.IsPaneOpen = !NavView.IsPaneOpen;

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
