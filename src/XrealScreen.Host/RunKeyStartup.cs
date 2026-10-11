using Microsoft.Win32;

namespace XrealScreen.Host;

/// <summary>Start-with-Windows state, shared by the MSIX StartupTask and the Run key (ADR-0013).</summary>
public enum StartupState
{
    Disabled,
    Enabled,

    /// <summary>The user turned it off in Task Manager → Startup apps; only they can turn it back on there.</summary>
    DisabledByUser,

    DisabledByPolicy,
}

/// <summary>
/// "Start with Windows" for the unpackaged (MSI-installed) app: a value under HKCU\…\Run (ADR-0013). Task Manager's
/// Startup apps switch writes HKCU\…\Explorer\StartupApproved\Run; an odd first byte there means disabled by the user.
/// </summary>
public sealed class RunKeyStartup
{
    public const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    public const string ApprovedKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";

    private readonly RegistryKey _root;
    private readonly string _runKeyPath;
    private readonly string _approvedKeyPath;

    /// <param name="valueName">Name of the Run value (e.g. "XrealScreen").</param>
    /// <param name="command">Command line Windows runs at sign-in (quoted exe path + arguments).</param>
    /// <param name="root">Registry root (default HKCU; tests pass a scratch key).</param>
    /// <param name="runKeyPath">Run key path under <paramref name="root"/>.</param>
    /// <param name="approvedKeyPath">StartupApproved\Run key path under <paramref name="root"/>.</param>
    public RunKeyStartup(string valueName, string command, RegistryKey? root = null,
        string runKeyPath = RunKeyPath, string approvedKeyPath = ApprovedKeyPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(valueName);
        ArgumentException.ThrowIfNullOrWhiteSpace(command);
        ValueName = valueName;
        Command = command;
        _root = root ?? Registry.CurrentUser;
        _runKeyPath = runKeyPath;
        _approvedKeyPath = approvedKeyPath;
    }

    public string ValueName { get; }

    public string Command { get; }

    public StartupState State
    {
        get
        {
            using var run = _root.OpenSubKey(_runKeyPath);
            if (run?.GetValue(ValueName) is not string)
            {
                return StartupState.Disabled;
            }

            return IsDisabledByUser() ? StartupState.DisabledByUser : StartupState.Enabled;
        }
    }

    /// <summary>Writes the Run value (also refreshing a stale exe path). Returns the resulting state.</summary>
    public StartupState Enable()
    {
        using (var run = _root.CreateSubKey(_runKeyPath, writable: true))
        {
            run.SetValue(ValueName, Command, RegistryValueKind.String);
        }

        return State;
    }

    public StartupState Disable()
    {
        using (var run = _root.OpenSubKey(_runKeyPath, writable: true))
        {
            run?.DeleteValue(ValueName, throwOnMissingValue: false);
        }

        return State;
    }

    private bool IsDisabledByUser()
    {
        using var approved = _root.OpenSubKey(_approvedKeyPath);
        return approved?.GetValue(ValueName) is byte[] { Length: > 0 } flags && (flags[0] & 1) == 1;
    }
}
