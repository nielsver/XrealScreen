using System.Text.Json;
using System.Text.Json.Serialization;

namespace XrealScreen.App;

/// <summary>App-level preferences (not workspace options). Stored in %LOCALAPPDATA%\XrealScreen\app.json.</summary>
public sealed class AppPreferences
{
    /// <summary>Closing the window hides XrealScreen to the tray instead of quitting.</summary>
    public bool KeepRunningInTray { get; set; } = true;

    /// <summary>The "still running in the tray" notification was shown once.</summary>
    public bool TrayHintShown { get; set; }

    /// <summary>Start the latest workspace (the last used settings) when XrealScreen starts.</summary>
    public bool StartWorkspaceOnLaunch { get; set; }

    /// <summary>Start <see cref="AutoStartWorkspaceName"/> when the glasses are plugged in.</summary>
    public bool StartWorkspaceWhenGlassesConnect { get; set; }

    /// <summary>Saved workspace used by the automatic starts; null = the latest settings.</summary>
    public string? AutoStartWorkspaceName { get; set; }

    /// <summary>Saved workspace last saved or loaded (preselected on the Home page).</summary>
    public string? LastWorkspaceName { get; set; }

    /// <summary>Games page: head look mapping (ADR-0011).</summary>
    public XrealScreen.Core.Gaming.HeadMouseSettings HeadLook { get; set; } = new();

    /// <summary>Games page: make the glasses the primary monitor while head look runs.</summary>
    public bool HeadLookGlassesPrimary { get; set; } = true;

    /// <summary>Local API for developers (ADR-0012): named pipe <c>\\.\pipe\XrealScreen.Api</c> for the current user.</summary>
    public bool EnableLocalApi { get; set; } = true;

    /// <summary>0 = system, 1 = light, 2 = dark.</summary>
    public int Theme { get; set; }

    /// <summary>Width of the open navigation pane (dragged at its right edge).</summary>
    public double NavPaneWidth { get; set; } = 320;

    /// <summary>Navigation pane open (icons and text) or collapsed to icons.</summary>
    public bool NavPaneOpen { get; set; } = true;

    private static string FilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "XrealScreen", "app.json");

    public static AppPreferences Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                return JsonSerializer.Deserialize(File.ReadAllText(FilePath), AppPreferencesJsonContext.Default.AppPreferences) ?? new AppPreferences();
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            CrashLog.Write("Preferences", ex);
        }

        return new AppPreferences();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, AppPreferencesJsonContext.Default.AppPreferences));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            CrashLog.Write("Preferences", ex);
        }
    }
}

[JsonSerializable(typeof(AppPreferences))]
[JsonSourceGenerationOptions(WriteIndented = true)]
internal sealed partial class AppPreferencesJsonContext : JsonSerializerContext;
