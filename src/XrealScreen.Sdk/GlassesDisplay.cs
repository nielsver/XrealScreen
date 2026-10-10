using System.Drawing;
using XrealScreen.Core.Devices;
using XrealScreen.Display;

namespace XrealScreen.Sdk;

/// <summary>The glasses as a Windows monitor.</summary>
/// <param name="DeviceName">GDI device name, e.g. <c>\.\DISPLAY3</c>. Changes between reboots; do not store it.</param>
/// <param name="FriendlyName">Monitor name from the EDID, e.g. <c>XREAL 1S</c>.</param>
/// <param name="Model">Glasses model, or null when the EDID is not in the catalog.</param>
/// <param name="Bounds">Desktop rectangle in physical pixels (virtual-screen coordinates).</param>
/// <param name="RefreshHz">Current refresh rate.</param>
/// <param name="IsPrimary">True when the glasses are the Windows primary monitor (at 0,0).</param>
public sealed record GlassesMonitor(string DeviceName, string FriendlyName, GlassesModel? Model, Rectangle Bounds, double RefreshHz, bool IsPrimary);

/// <summary>Finds the glasses monitor and puts games or windows on it.</summary>
public static class GlassesDisplay
{
    /// <summary>The glasses monitor, or null when the glasses are not connected as a display.</summary>
    public static GlassesMonitor? Find()
    {
        var m = GlassesDisplayLocator.FindGlasses(new CcdDisplayTopology().GetActiveMonitors());
        return m is null
            ? null
            : new GlassesMonitor(m.GdiDeviceName, m.FriendlyName, GlassesDisplayLocator.Identify(m), new Rectangle(m.X, m.Y, m.Resolution.Width, m.Resolution.Height), m.RefreshHz, m.IsPrimary);
    }

    /// <summary>
    /// Makes the glasses the Windows primary monitor, so games (and every exclusive-fullscreen game)
    /// open on them. Dispose the returned session to restore the previous layout.
    /// </summary>
    /// <returns>The session, or null when the glasses already are the primary monitor.</returns>
    /// <remarks>
    /// The previous layout is saved to <c>%LOCALAPPDATA%\XrealScreen\topology-snapshot.json</c>
    /// first, so the XrealScreen app (at start) or <c>xrs recover</c> restores it after a crash.
    /// </remarks>
    /// <exception cref="InvalidOperationException">No glasses monitor, or a workspace (or a crashed session) owns the snapshot.</exception>
    /// <exception cref="System.ComponentModel.Win32Exception">Windows rejected the new layout.</exception>
    public static Task<GlassesPrimarySession?> MakePrimaryAsync(CancellationToken cancellationToken = default) =>
        GlassesPrimarySession.StartAsync(cancellationToken);

    /// <summary>
    /// Moves the foreground window onto the glasses: a borderless-fullscreen window fills them, other
    /// windows are centered. Exclusive-fullscreen games cannot be moved (use <see cref="MakePrimaryAsync"/>).
    /// </summary>
    /// <returns>The window title, or null when nothing was moved.</returns>
    public static string? MoveForegroundWindowToGlasses() =>
        Find() is { } glasses ? WindowMover.MoveForegroundWindow(glasses.Bounds) : null;
}
