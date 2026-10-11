using System.Runtime.InteropServices;
using Microsoft.Windows.AppLifecycle;
using StartupTask = Windows.ApplicationModel.StartupTask;
using StartupTaskState = Windows.ApplicationModel.StartupTaskState;
using XrealScreen.Host;

namespace XrealScreen.App;

/// <summary>
/// "Start with Windows" for both ways the app is installed (ADR-0013): the MSIX package (and the dev
/// registration) uses its StartupTask; the MSI-installed, unpackaged app uses an HKCU Run value.
/// </summary>
public static partial class AppStartup
{
    /// <summary>StartupTask id declared in Package.appxmanifest.</summary>
    public const string StartupTaskId = "XrealScreenStartup";

    /// <summary>Argument the Run value passes, so the app knows Windows started it.</summary>
    public const string AutostartArgument = "--autostart";

    private const int AppModelErrorNoPackage = 15700;

    /// <summary>True when the process has package identity (MSIX install or dev registration).</summary>
    public static bool IsPackaged { get; } = HasPackageIdentity();

    /// <summary>True when Windows started the app at sign-in (it then stays in the tray).</summary>
    public static bool StartedByWindows => IsPackaged
        ? AppInstance.GetCurrent().GetActivatedEventArgs().Kind == ExtendedActivationKind.StartupTask
        : Environment.GetCommandLineArgs().Contains(AutostartArgument, StringComparer.OrdinalIgnoreCase);

    public static async Task<StartupState> GetStateAsync()
    {
        if (!IsPackaged)
        {
            var runKey = NewRunKey();
            // Keep the Run value pointing at this exe (e.g. after the user moved the install folder).
            return runKey.State == StartupState.Enabled ? runKey.Enable() : runKey.State;
        }

        var task = await StartupTask.GetAsync(StartupTaskId);
        return Map(task.State);
    }

    public static async Task<StartupState> SetEnabledAsync(bool enabled)
    {
        if (!IsPackaged)
        {
            var runKey = NewRunKey();
            return enabled ? runKey.Enable() : runKey.Disable();
        }

        var task = await StartupTask.GetAsync(StartupTaskId);
        if (enabled)
        {
            return Map(await task.RequestEnableAsync());
        }

        task.Disable();
        return Map(task.State);
    }

    private static RunKeyStartup NewRunKey() =>
        new("XrealScreen", $"\"{Environment.ProcessPath}\" {AutostartArgument}");

    private static StartupState Map(StartupTaskState state) => state switch
    {
        StartupTaskState.Enabled or StartupTaskState.EnabledByPolicy => StartupState.Enabled,
        StartupTaskState.DisabledByUser => StartupState.DisabledByUser,
        StartupTaskState.DisabledByPolicy => StartupState.DisabledByPolicy,
        _ => StartupState.Disabled,
    };

    private static bool HasPackageIdentity()
    {
        uint length = 0;
        return GetCurrentPackageFullName(ref length, IntPtr.Zero) != AppModelErrorNoPackage;
    }

    [LibraryImport("kernel32.dll")]
    private static partial int GetCurrentPackageFullName(ref uint packageFullNameLength, IntPtr packageFullName);
}
