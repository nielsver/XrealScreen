using Microsoft.UI.Xaml.Controls;
using XrealScreen.App.ViewModels;

namespace XrealScreen.App.Pages;

public sealed partial class GamesPage : Page
{
    public GamesPage() => InitializeComponent();

    public HeadLookViewModel HeadLook => App.HeadLook;
}
