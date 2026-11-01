using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Xaml.Controls;
using XrealScreen.Device.XrealOne;

namespace XrealScreen.App.ViewModels;

/// <summary>One label/value row in the Glasses panel.</summary>
public sealed record GlassesDetail(string Label, string Value);

/// <summary>Device page: checks the glasses' USB network link and ports (same as `xrs probe`) and shows what the glasses report (`xrs device info`).</summary>
public sealed partial class DeviceViewModel : ObservableObject
{
    public ObservableCollection<string> Results { get; } = [];

    /// <summary>Firmware, serial number and cameras read over the control port (read-only, no update check).</summary>
    public ObservableCollection<GlassesDetail> GlassesDetails { get; } = [];

    [ObservableProperty]
    public partial string StatusTitle { get; set; } = "Not checked";

    [ObservableProperty]
    public partial string StatusMessage { get; set; } = "Plug in the glasses and select Check connection.";

    [ObservableProperty]
    public partial InfoBarSeverity Severity { get; set; } = InfoBarSeverity.Informational;

    [ObservableProperty]
    public partial string GlassesHint { get; set; } = "Read after Check connection, or select Read glasses info.";

    [RelayCommand]
    private async Task CheckAsync(CancellationToken cancellationToken)
    {
        Results.Clear();
        StatusTitle = "Checking…";
        StatusMessage = string.Empty;
        Severity = InfoBarSeverity.Informational;

        var endpoints = OneEndpoints.Default;
        var adapters = await Task.Run(() => NcmAdapterLocator.FindCandidates(endpoints.Host), cancellationToken).ConfigureAwait(true);
        if (adapters.Count == 0)
        {
            Results.Add("No USB network adapter on 169.254.2.x found.");
        }

        foreach (var a in adapters)
        {
            Results.Add($"Adapter: {a.Description} ({a.LocalAddress}, {a.Status})");
        }

        bool streamOpen = false;
        bool controlOpen = false;
        foreach (var (name, port) in endpoints.AllPorts())
        {
            var elapsed = await NcmAdapterLocator.TryConnectAsync(endpoints.Host, port, TimeSpan.FromSeconds(1.5), cancellationToken).ConfigureAwait(true);
            Results.Add($"{endpoints.Host}:{port} ({name}): {(elapsed is null ? "not reachable" : $"open, {elapsed.Value.TotalMilliseconds:F0} ms")}");
            streamOpen |= name == "stream" && elapsed is not null;
            controlOpen |= name == "control" && elapsed is not null;
        }

        if (streamOpen)
        {
            StatusTitle = "Glasses reachable";
            StatusMessage = "The IMU stream port is open. Start head tracking on the Tracking page.";
            Severity = InfoBarSeverity.Success;
        }
        else
        {
            StatusTitle = "Glasses not reachable";
            StatusMessage = "No IMU stream found. See docs/testing/hardware-test-M1.md.";
            Severity = InfoBarSeverity.Warning;
        }

        if (controlOpen)
        {
            await ReadGlassesInfoAsync(cancellationToken).ConfigureAwait(true);
        }
    }

    [RelayCommand]
    private async Task ReadGlassesInfoAsync(CancellationToken cancellationToken)
    {
        GlassesDetails.Clear();
        GlassesHint = "Reading…";
        GlassesInfo info;
        try
        {
            info = await Task.Run(() => GlassesInfo.ReadAsync(OneEndpoints.Default, cancellationToken), cancellationToken).ConfigureAwait(true);
        }
        catch (Exception ex) when (ex is IOException or System.Net.Sockets.SocketException or TimeoutException or InvalidDataException)
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
