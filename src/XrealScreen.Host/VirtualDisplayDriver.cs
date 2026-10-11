using XrealScreen.Display.VirtualDisplayRs;

namespace XrealScreen.Host;

/// <summary>
/// Whether the virtual display driver (virtual-display-rs, ADR-0008) is installed and running. The Store package
/// cannot install it (MSIX cannot install drivers, ADR-0005), so the app checks and points to the driver installer.
/// </summary>
public static class VirtualDisplayDriver
{
    /// <summary>Where users get the separate driver installer.</summary>
    public const string DownloadUrl = "https://github.com/nikryden/Screen-XREAL-1S/releases/latest";

    /// <summary>True when the driver answers on its pipe (takes up to ~3 s when it is missing).</summary>
    public static async Task<bool> IsAvailableAsync(CancellationToken cancellationToken)
    {
        using var provider = new VirtualDisplayRsProvider();
        return await provider.IsAvailableAsync(cancellationToken).ConfigureAwait(false);
    }
}
