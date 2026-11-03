using System.Globalization;
using System.Text.Json;

namespace XrealScreen.Device.XrealOne;

/// <summary>Camera entry of the factory calibration (pinhole intrinsics in pixels).</summary>
public sealed record CameraCalibration(int Width, int Height, double FocalX, double FocalY, double CenterX, double CenterY)
{
    /// <summary>Horizontal field of view from the intrinsics, degrees.</summary>
    public double HorizontalFovDegrees => 2 * Math.Atan(Width / 2.0 / FocalX) * 180 / Math.PI;

    /// <summary>Vertical field of view from the intrinsics, degrees.</summary>
    public double VerticalFovDegrees => 2 * Math.Atan(Height / 2.0 / FocalY) * 180 / Math.PI;
}

/// <summary>The parts of the factory calibration XrealScreen uses.</summary>
public sealed record GlassesCalibration(int? GlassesVersion, string? LastModified, CameraCalibration? RgbCamera, CameraCalibration? SlamCamera)
{
    public static GlassesCalibration Parse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        return new GlassesCalibration(
            root.TryGetProperty("glasses_version", out var v) && v.ValueKind == JsonValueKind.Number ? v.GetInt32() : null,
            root.TryGetProperty("last_modified_time", out var t) && t.ValueKind == JsonValueKind.String ? t.GetString() : null,
            ReadCamera(root, "RGB_camera"),
            ReadCamera(root, "SLAM_camera"));
    }

    private static CameraCalibration? ReadCamera(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var camera) || !camera.TryGetProperty("device_1", out var device)
            || !device.TryGetProperty("resolution", out var resolution) || resolution.GetArrayLength() < 2)
        {
            return null;
        }

        // Resolutions come as ints (RGB) or floats (SLAM); intrinsics are optional.
        double At(string property, int index) =>
            device.TryGetProperty(property, out var values) && values.GetArrayLength() > index ? values[index].GetDouble() : double.NaN;
        return new CameraCalibration(
            (int)resolution[0].GetDouble(), (int)resolution[1].GetDouble(),
            At("fc", 0), At("fc", 1), At("cc", 0), At("cc", 1));
    }
}

/// <summary>What the glasses report about themselves over the control port (read-only).</summary>
public sealed record GlassesInfo(string SoftwareVersion, string DspVersion, string SerialNumber, GlassesCalibration? Calibration)
{
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(3);

    /// <summary>
    /// Firmware as far as the glasses report it. XREAL's updater shows a longer string whose first part
    /// ("15.1.03.603_…") is not available on the control port [hypothesis: MCU over HID].
    /// </summary>
    public string Firmware => string.Create(CultureInfo.InvariantCulture, $"{DspVersion}_{SoftwareVersion}");

    /// <summary>Connects to the control port and reads versions, serial number and calibration.</summary>
    public static async Task<GlassesInfo> ReadAsync(OneEndpoints? endpoints, CancellationToken cancellationToken)
    {
        await using var client = await XrealControlClient.ConnectAsync(endpoints, DefaultTimeout, cancellationToken).ConfigureAwait(false);
        string software = await client.GetSoftwareVersionAsync(DefaultTimeout, cancellationToken).ConfigureAwait(false);
        string dsp = await client.GetDspVersionAsync(DefaultTimeout, cancellationToken).ConfigureAwait(false);
        string serial = await client.GetSerialNumberAsync(DefaultTimeout, cancellationToken).ConfigureAwait(false);
        GlassesCalibration? calibration = null;
        try
        {
            string json = await client.GetConfigJsonAsync(TimeSpan.FromSeconds(10), cancellationToken).ConfigureAwait(false);
            calibration = GlassesCalibration.Parse(json);
        }
        catch (Exception ex) when (ex is JsonException or InvalidDataException or TimeoutException)
        {
            // Versions are still useful without the calibration.
        }

        return new GlassesInfo(software, dsp, serial, calibration);
    }
}
