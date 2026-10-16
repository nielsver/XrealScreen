using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using XrealScreen.App.ViewModels;

namespace XrealScreen.App.Controls;

/// <summary>Send the current window to a virtual screen, and put the cursor on one.</summary>
public sealed partial class DeskActions : UserControl
{
    public static readonly DependencyProperty ShowHelpProperty = DependencyProperty.Register(
        nameof(ShowHelp), typeof(bool), typeof(DeskActions), new PropertyMetadata(true));

    public DeskActions() => InitializeComponent();

    public WorkspaceViewModel Workspace => App.Workspace;

    public SessionViewModel Session => App.Session;

    /// <summary>False on a tight dock, where the buttons and their tips are enough.</summary>
    public bool ShowHelp
    {
        get => (bool)GetValue(ShowHelpProperty);
        set => SetValue(ShowHelpProperty, value);
    }

    private void Send1_Click(object sender, RoutedEventArgs e) => Session.SendWindowTo(1);

    private void Send2_Click(object sender, RoutedEventArgs e) => Session.SendWindowTo(2);

    private void Send3_Click(object sender, RoutedEventArgs e) => Session.SendWindowTo(3);

    private void Send4_Click(object sender, RoutedEventArgs e) => Session.SendWindowTo(4);

    private void Send5_Click(object sender, RoutedEventArgs e) => Session.SendWindowTo(5);

    private void Send6_Click(object sender, RoutedEventArgs e) => Session.SendWindowTo(6);

    private void Find_Click(object sender, RoutedEventArgs e) => Session.FindCursor();
}
