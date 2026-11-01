using System.Text.Json;
using XrealScreen.Vision.Hands;

namespace XrealScreen.Host.Hands;

/// <summary>The user's hand calibration in <c>%LOCALAPPDATA%\XrealScreen\hand-calibration.json</c>.</summary>
public sealed class HandCalibrationStore(string? path = null)
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public string Path { get; } = path ?? System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "XrealScreen", "hand-calibration.json");

    public sealed record Saved(HandCalibration Calibration, double RmsError, DateTimeOffset Created, int Points);

    public Saved? Load()
    {
        try
        {
            return File.Exists(Path) ? JsonSerializer.Deserialize<Saved>(File.ReadAllText(Path), Json) : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public void Save(Saved saved)
    {
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
        File.WriteAllText(Path, JsonSerializer.Serialize(saved, Json));
    }
}
