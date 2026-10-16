using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Xaml.Controls;
using XrealScreen.Host;

namespace XrealScreen.App.ViewModels;

/// <summary>Device page: connect checks the picture and the USB link, and sends the workspace picture mode.</summary>
public sealed partial class DeviceViewModel : ObservableObject
{
    public ObservableCollection<string> Results { get; } = [];

    [ObservableProperty]
    public partial string StatusTitle { get; set; } = "Not checked";

    [ObservableProperty]
    public partial string StatusMessage { get; set; } = "Plug in the glasses, then choose Connect with glasses.";

    [ObservableProperty]
    public partial InfoBarSeverity Severity { get; set; } = InfoBarSeverity.Informational;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CheckCommand))]
    [NotifyCanExecuteChangedFor(nameof(SendSetupCommand))]
    public partial bool IsBusy { get; set; }

    private bool CanRun() => !IsBusy;

    [RelayCommand(CanExecute = nameof(CanRun))]
    private Task CheckAsync(CancellationToken cancellationToken) => RunAsync(sendSetup: false, cancellationToken);

    [RelayCommand(CanExecute = nameof(CanRun))]
    private Task SendSetupAsync(CancellationToken cancellationToken) => RunAsync(sendSetup: true, cancellationToken);

    private async Task RunAsync(bool sendSetup, CancellationToken cancellationToken)
    {
        Results.Clear();
        IsBusy = true;
        StatusTitle = sendSetup ? "Sending setup…" : "Connecting…";
        StatusMessage = sendSetup
            ? "Switching the glasses picture to the mode this workspace uses."
            : "Checking the picture, the USB network, the ports, and a short head-tracking sample.";
        Severity = InfoBarSeverity.Informational;

        try
        {
            bool anchor = App.Workspace.IsGlassesAnchor;
            GlassesLinkReport report = sendSetup
                ? await GlassesLink.SendSetupAsync(anchor, cancellationToken).ConfigureAwait(true)
                : await GlassesLink.CheckAsync(anchor, cancellationToken).ConfigureAwait(true);
            foreach (string line in report.Lines)
            {
                Results.Add(line);
            }

            StatusTitle = report.Title;
            StatusMessage = report.Message;
            Severity = report.Title switch
            {
                "Glasses connected" => InfoBarSeverity.Success,
                "Glasses not connected" => InfoBarSeverity.Warning,
                "Picture connected — data link not reachable" when anchor => InfoBarSeverity.Informational,
                _ => InfoBarSeverity.Warning,
            };
            App.Workspace.RefreshGlassesSignal();
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            StatusTitle = "Check stopped";
            StatusMessage = "The glasses did not answer in time.";
            Severity = InfoBarSeverity.Warning;
        }
        finally
        {
            IsBusy = false;
        }
    }
}
