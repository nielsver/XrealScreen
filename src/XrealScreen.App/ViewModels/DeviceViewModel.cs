using System.Collections.ObjectModel;
using System.Net.Sockets;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Xaml.Controls;
using XrealScreen.Device.XrealOne;
using XrealScreen.Host;

namespace XrealScreen.App.ViewModels;

/// <summary>One label/value row in the Glasses panel.</summary>
public sealed record GlassesDetail(string Label, string Value);

/// <summary>Device page: connect checks the picture and the USB link, sends the workspace picture mode, and reads what the glasses report.</summary>
public sealed partial class DeviceViewModel : ObservableObject
{
    public ObservableCollection<string> Results { get; } = [];

    /// <summary>Firmware, serial number and cameras read over the control port (read-only, no update check).</summary>
    public ObservableCollection<GlassesDetail> GlassesDetails { get; } = [];

    [ObservableProperty]
    public partial string StatusTitle { get; set; } = "Not checked";

    [ObservableProperty]
    public partial string StatusMessage { get; set; } = "Plug in the glasses, then choose Connect with glasses.";

    [ObservableProperty]
    public partial InfoBarSeverity Severity { get; set; } = InfoBarSeverity.Informational;

    [ObservableProperty]
    public partial string GlassesHint { get; set; } = "Read after Connect with glasses, or select Read glasses info.";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CheckCommand))]
    [NotifyCanExecuteChangedFor(nameof(SendSetupCommand))]
    [NotifyCanExecuteChangedFor(nameof(ReadGlassesInfoCommand))]
    public partial bool IsBusy { get; set; }

    private bool CanRun() => !IsBusy;

    [RelayCommand(CanExecute = nameof(CanRun))]
    private Task CheckAsync(CancellationToken cancellationToken) => RunAsync(sendSetup: false, cancellationToken);

    [RelayCommand(CanExecute = nameof(CanRun))]
    private Task SendSetupAsync(CancellationToken cancellationToken) => RunAsync(sendSetup: true, cancellationToken);

    [RelayCommand(CanExecute = nameof(CanRun))]
    private async Task ReadGlassesInfoAsync(CancellationToken cancellationToken)
    {
        IsBusy = true;
        try
        {
            await ReadGlassesInfoCoreAsync(cancellationToken).ConfigureAwait(true);
        }
        finally
        {
            IsBusy = false;
        }
    }

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
            if (report.Title == "Glasses connected")
            {
                await ReadGlassesInfoCoreAsync(cancellationToken).ConfigureAwait(true);
            }
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

    private async Task ReadGlassesInfoCoreAsync(CancellationToken cancellationToken)
    {
        GlassesDetails.Clear();
        GlassesHint = "Reading…";
        GlassesInfo info;
        try
        {
            info = await Task.Run(() => GlassesInfo.ReadAsync(OneEndpoints.Default, cancellationToken), cancellationToken).ConfigureAwait(true);
        }
        catch (Exception ex) when (ex is IOException or SocketException or TimeoutException or InvalidDataException)
        {
            GlassesHint = $"Could not read the glasses: {ex.Message}";
            return;
        }

        GlassesDetails.Add(new("Software version", info.SoftwareVersion));
        GlassesDetails.Add(new("DSP version", info.DspVersion));
        GlassesDetails.Add(new("Serial number", info.SerialNumber));
        if (info.Calibration is { } c)
        {
            GlassesDetails.Add(new("Colour camera", Describe(c.RgbCamera)));
            GlassesDetails.Add(new("Tracking camera", Describe(c.SlamCamera)));
            GlassesDetails.Add(new("Factory calibration", $"version {c.GlassesVersion}, {c.LastModified}"));
        }

        GlassesHint = "XREAL's updater (xreal.com/ota) shows the full firmware, including a leading 15.x part the glasses do not report here.";
    }

    private static string Describe(CameraCalibration? camera) =>
        camera is null ? "none" : $"{camera.Width} × {camera.Height}, field of view {camera.HorizontalFovDegrees:F0}° × {camera.VerticalFovDegrees:F0}°";
}
