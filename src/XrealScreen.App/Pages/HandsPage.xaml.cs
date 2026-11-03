using Microsoft.UI.Xaml.Controls;
using XrealScreen.App.ViewModels;

namespace XrealScreen.App.Pages;

public sealed partial class HandsPage : Page
{
    public HandsPage() => InitializeComponent();

    public HandsViewModel Hands => App.Hands;
}
