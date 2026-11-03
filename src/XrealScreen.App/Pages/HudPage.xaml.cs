using Microsoft.UI.Xaml.Controls;
using XrealScreen.App.ViewModels;

namespace XrealScreen.App.Pages;

public sealed partial class HudPage : Page
{
    public HudPage() => InitializeComponent();

    public HudViewModel Hud => App.Hud;
}
