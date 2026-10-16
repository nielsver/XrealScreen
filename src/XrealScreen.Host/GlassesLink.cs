using XrealScreen.Core.Abstractions;
using XrealScreen.Core.Devices;
using XrealScreen.Core.Workspace;
using XrealScreen.Device.XrealOne;
using XrealScreen.Display;

namespace XrealScreen.Host;

/// <summary>What Connect and Send setup found. <see cref="Lines"/> is the checklist.</summary>
public sealed record GlassesLinkReport(string Title, string Message, IReadOnlyList<string> Lines);

/// <summary>
/// Checks the glasses picture and the USB data link, and sends the picture mode the current workspace needs.
/// Anchor and Follow stay on the glasses' X button; that switch is not on the USB link.
/// </summary>
public static class GlassesLink
{
    public static Task<GlassesLinkReport> CheckAsync(bool glassesAnchor, CancellationToken cancellationToken) =>
        InspectAsync(glassesAnchor, sendSetup: false, cancellationToken);

    public static Task<GlassesLinkReport> SendSetupAsync(bool glassesAnchor, CancellationToken cancellationToken) =>
        InspectAsync(glassesAnchor, sendSetup: true, cancellationToken);

    private static async Task<GlassesLinkReport> InspectAsync(bool glassesAnchor, bool sendSetup, CancellationToken cancellationToken)
    {
        var lines = new List<string>();
        var topology = new CcdDisplayTopology();
        DisplayMonitor? glasses = FindGlasses(topology);
        if (glasses is null)
        {
            lines.Add("Picture: no glasses monitor. Plug them into USB-C. Windows has to extend the desktop onto them.");
        }
        else
        {
            lines.Add($"Picture: {Describe(glasses)}.");
        }

        if (sendSetup && glasses is not null)
        {
            lines.Add(await ApplyPictureAsync(topology, glasses, glassesAnchor, cancellationToken).ConfigureAwait(false));
            glasses = FindGlasses(topology) ?? glasses;
        }
        else if (glasses is not null)
        {
            lines.Add(PictureFits(glasses, glassesAnchor));
        }

        var endpoints = OneEndpoints.Default;
        var adapters = NcmAdapterLocator.FindCandidates(endpoints.Host);
        if (adapters.Count == 0)
        {
            lines.Add("USB network: not found on 169.254.2.x. The picture can stay on while this link is down. Unplug the glasses, wait a few seconds, and plug them in again.");
        }
        else
        {
            foreach (var adapter in adapters)
            {
                lines.Add($"USB network: {adapter.Description}, {adapter.LocalAddress}, {adapter.Status}.");
            }
        }

        bool streamOpen = false;
        bool controlOpen = false;
        foreach (var (name, port) in endpoints.AllPorts())
        {
            var elapsed = await NcmAdapterLocator.TryConnectAsync(endpoints.Host, port, TimeSpan.FromSeconds(1.5), cancellationToken).ConfigureAwait(false);
            bool open = elapsed is not null;
            lines.Add($"{endpoints.Host}:{port} {PortLabel(name)}: {(open ? $"open, {elapsed!.Value.TotalMilliseconds:0} ms" : "not reachable")}.");
            streamOpen |= name == "stream" && open;
            controlOpen |= name == "control" && open;
        }

        bool identity = false;
        if (controlOpen)
        {
            identity = await ReadIdentityAsync(endpoints, lines, cancellationToken).ConfigureAwait(false);
            if (sendSetup)
            {
                await SendControlSetupAsync(endpoints, lines, cancellationToken).ConfigureAwait(false);
            }
        }
        else if (sendSetup)
        {
            lines.Add("Setup commands were not sent over USB because the control port is not reachable.");
        }

        bool imu = false;
        if (streamOpen)
        {
            var sample = await OneStreamSample.ReadAsync(endpoints.Host, endpoints.StreamPort, TimeSpan.FromMilliseconds(400), cancellationToken).ConfigureAwait(false);
            if (sample.Error is not null && sample.ImuReports == 0)
            {
                lines.Add($"IMU stream: {sample.Error}.");
            }
            else
            {
                imu = sample.ImuReports > 0;
                lines.Add(imu
                    ? $"IMU stream: {sample.ImuReports} reports in 0.4 s (about {sample.ImuReports / 0.4:0} Hz)."
                    : "IMU stream: the port opened but sent no reports.");
            }
        }

        lines.Add(glassesAnchor
            ? "Anchor or Follow is the X button on the glasses. For this workspace, single-click X until it says Anchor."
            : "Anchor or Follow is the X button on the glasses. For head tracking, single-click X until it says Follow, then turn Stabilizer off.");

        bool picture = glasses is not null;
        bool data = imu || identity;
        string title = picture && data
            ? "Glasses connected"
            : picture
                ? "Picture connected — data link not reachable"
                : data
                    ? "Data link connected — no picture"
                    : "Glasses not connected";
        string message = string.Join(" ", lines.Take(4));
        if (lines.Count > 4)
        {
            message += " The Device page lists every check.";
        }

        return new GlassesLinkReport(title, message, lines);
    }

    private static DisplayMonitor? FindGlasses(CcdDisplayTopology topology)
    {
        try
        {
            return GlassesDisplayLocator.FindGlasses(topology.GetActiveMonitors());
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return null;
        }
    }

    private static async Task<string> ApplyPictureAsync(CcdDisplayTopology topology, DisplayMonitor glasses, bool glassesAnchor, CancellationToken cancellationToken)
    {
        IReadOnlyList<DisplayMode> modes;
        try
        {
            modes = topology.GetSupportedModes(glasses.GdiDeviceName);
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            return $"Picture mode: could not read the modes Windows offers ({ex.Message}).";
        }

        DisplayMode? wanted = GlassesPictureSetup.Choose(glassesAnchor, modes);
        if (wanted is null)
        {
            return glassesAnchor
                ? "Picture mode: these glasses do not offer an ultrawide mode, so the anchor setup cannot be sent."
                : "Picture mode: these glasses do not offer a normal high-rate mode, so the head-tracking setup cannot be sent.";
        }

        string target = $"{wanted.Value.Resolution} @ {wanted.Value.RefreshHz} Hz";
        if (SameMode(glasses, wanted.Value))
        {
            return $"Picture mode: already {target}.";
        }

        bool ok = topology.TrySetMode(glasses.GdiDeviceName, wanted.Value);
        if (!ok)
        {
            return $"Picture mode: Windows rejected {target}.";
        }

        await Task.Delay(1200, cancellationToken).ConfigureAwait(false);
        var now = FindGlasses(topology);
        return now is not null && SameMode(now, wanted.Value)
            ? $"Picture mode: sent {target}."
            : $"Picture mode: Windows accepted {target}. The glasses now show {(now is null ? "no picture" : Describe(now))}.";
    }

    private static string PictureFits(DisplayMonitor glasses, bool glassesAnchor)
    {
        bool ultra = AnchorSplit.IsUltrawideSignal(glasses.Resolution);
        if (glassesAnchor)
        {
            return ultra
                ? "Picture matches glasses anchor."
                : "Picture is not ultrawide. Send setup asks for 3840×1080.";
        }

        return ultra
            ? "Picture is ultrawide. Send setup asks for a normal picture for head tracking."
            : "Picture matches head tracking.";
    }

    private static async Task<bool> ReadIdentityAsync(OneEndpoints endpoints, List<string> lines, CancellationToken cancellationToken)
    {
        try
        {
            await using var control = await OneControlClient.ConnectAsync(endpoints.Host, endpoints.ControlPort, TimeSpan.FromSeconds(1.5), cancellationToken).ConfigureAwait(false);
            string id = await control.GetIdAsync(cancellationToken).ConfigureAwait(false);
            string version = await control.GetSoftwareVersionAsync(cancellationToken).ConfigureAwait(false);
            lines.Add($"Glasses identity: {id}, firmware {version}.");
            return true;
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or OperationCanceledException or System.Net.Sockets.SocketException)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                throw;
            }

            lines.Add($"Control port opened, but the glasses did not answer ({ex.Message}).");
            return false;
        }
    }

    private static async Task SendControlSetupAsync(OneEndpoints endpoints, List<string> lines, CancellationToken cancellationToken)
    {
        try
        {
            await using var control = await OneControlClient.ConnectAsync(endpoints.Host, endpoints.ControlPort, TimeSpan.FromSeconds(1.5), cancellationToken).ConfigureAwait(false);
            await control.SetRegularPictureAsync(cancellationToken).ConfigureAwait(false);
            lines.Add("Sent to the glasses: one picture, not side-by-side stereo.");
            await control.SetButtonsEnabledAsync(cancellationToken).ConfigureAwait(false);
            lines.Add("Sent to the glasses: buttons stay enabled.");
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or OperationCanceledException or System.Net.Sockets.SocketException)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                throw;
            }

            lines.Add($"Could not send the USB setup ({ex.Message}).");
        }
    }

    private static string Describe(DisplayMonitor glasses)
    {
        string? model = GlassesDisplayLocator.Identify(glasses)?.ToString();
        string name = string.IsNullOrWhiteSpace(glasses.FriendlyName) ? model ?? "Glasses" : glasses.FriendlyName;
        string ultra = AnchorSplit.IsUltrawideSignal(glasses.Resolution) ? "ultrawide" : "not ultrawide";
        int hz = (int)Math.Round(glasses.RefreshHz);
        return $"{name} ({glasses.EdidId}) on {glasses.GdiDeviceName}, {glasses.Resolution} @ {hz} Hz, {ultra}";
    }

    private static bool SameMode(DisplayMonitor glasses, DisplayMode mode) =>
        glasses.Resolution == mode.Resolution && (int)Math.Round(glasses.RefreshHz) == mode.RefreshHz;

    private static string PortLabel(string name) => name switch
    {
        "metadata" => "metadata",
        "camera" => "camera",
        "stream" => "IMU stream",
        "control" => "control",
        _ => name,
    };
}
