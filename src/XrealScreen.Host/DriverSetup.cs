using System.Security.Cryptography.X509Certificates;
using System.Text.Json;

namespace XrealScreen.Host;

/// <summary>What <see cref="DriverSetup.Install"/> did.</summary>
public enum DriverInstallOutcome
{
    /// <summary>A virtual-display-rs device already existed and was left alone (e.g. VertoXR's driver).</summary>
    KeptExisting,
    Installed,
    InstalledRebootRequired,
}

/// <summary>The virtual-display-rs device on this PC, if any.</summary>
public sealed record DriverStatus(bool DeviceExists, string? InstanceId, string? DriverVersion, string? PublishedInf, bool InstalledByXrealScreen);

/// <summary>
/// Installs and removes the bundled virtual display driver (virtual-display-rs, ADR-0008): trusts its test
/// certificate, creates the root-enumerated device <see cref="HardwareId"/> (what "devcon install" does) and installs
/// the driver on it. What was installed is recorded in <see cref="StateFile"/> so uninstall removes only that. Runs
/// elevated and without prompts — from the MSI (ADR-0013) or <c>xrs driver</c>. Same state file as the MSIX
/// installer's Install-Driver.ps1.
/// </summary>
public static class DriverSetup
{
    public const string HardwareId = @"Root\VirtualDisplayDriver";
    public const string InfName = "VirtualDisplayDriver.inf";
    public const string CertificateName = "XrealScreenDriver.cer";

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static string StateFile { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "XrealScreen", "driver-install.json");

    public static DriverStatus GetStatus()
    {
        var device = DeviceSetup.Find(HardwareId);
        return new DriverStatus(device is not null, device?.InstanceId, device?.DriverVersion, device?.InfPath, File.Exists(StateFile));
    }

    /// <param name="driverDir">Folder with <see cref="InfName"/>, its catalog/DLL and <see cref="CertificateName"/>.</param>
    /// <param name="update">Replace an existing virtual-display-rs driver with the bundled one.</param>
    public static DriverInstallOutcome Install(string driverDir, bool update)
    {
        string inf = Path.GetFullPath(Path.Combine(driverDir, InfName));
        string cer = Path.Combine(driverDir, CertificateName);
        if (!File.Exists(inf) || !File.Exists(cer))
        {
            throw new FileNotFoundException($"driver package not found in {driverDir}", inf);
        }

        var existing = DeviceSetup.Find(HardwareId);
        if (existing is not null && !update)
        {
            return DriverInstallOutcome.KeptExisting;
        }

        using var certificate = X509CertificateLoader.LoadCertificateFromFile(cer);
        foreach (var storeName in new[] { StoreName.Root, StoreName.TrustedPublisher })
        {
            using var store = new X509Store(storeName, StoreLocation.LocalMachine);
            store.Open(OpenFlags.ReadWrite);
            store.Add(certificate);
        }

        bool created = false;
        if (existing is null)
        {
            DeviceSetup.CreateRootDevice(HardwareId, DisplayClass, "Display", "Virtual Display");
            created = true;
        }

        bool reboot = DeviceSetup.UpdateDriver(HardwareId, inf);
        var installed = DeviceSetup.Find(HardwareId);

        Directory.CreateDirectory(Path.GetDirectoryName(StateFile)!);
        var state = new DriverInstallState
        {
            CreatedDevice = created,
            PublishedInf = installed?.InfPath,
            CertThumbprint = certificate.Thumbprint,
            Date = DateTime.Now.ToString("s", System.Globalization.CultureInfo.InvariantCulture),
        };
        File.WriteAllText(StateFile, JsonSerializer.Serialize(state, JsonOptions));
        return reboot ? DriverInstallOutcome.InstalledRebootRequired : DriverInstallOutcome.Installed;
    }

    /// <summary>Removes what <see cref="Install"/> recorded. False when XrealScreen installed nothing.</summary>
    public static bool Uninstall()
    {
        if (!File.Exists(StateFile))
        {
            return false;
        }

        var state = JsonSerializer.Deserialize<DriverInstallState>(File.ReadAllText(StateFile)) ?? new DriverInstallState();
        if (state.CreatedDevice)
        {
            DeviceSetup.RemoveDevices(HardwareId);
        }

        if (!string.IsNullOrEmpty(state.PublishedInf))
        {
            DeviceSetup.DeleteDriverPackage(state.PublishedInf);
        }

        if (!string.IsNullOrEmpty(state.CertThumbprint))
        {
            foreach (var storeName in new[] { StoreName.Root, StoreName.TrustedPublisher })
            {
                using var store = new X509Store(storeName, StoreLocation.LocalMachine);
                store.Open(OpenFlags.ReadWrite);
                foreach (var match in store.Certificates.Find(X509FindType.FindByThumbprint, state.CertThumbprint, validOnly: false))
                {
                    store.Remove(match);
                    match.Dispose();
                }
            }
        }

        File.Delete(StateFile);
        return true;
    }

    private static readonly Guid DisplayClass = new("4d36e968-e325-11ce-bfc1-08002be10318");

    // Property names match the JSON Install-Driver.ps1 writes.
    private sealed class DriverInstallState
    {
        public bool CreatedDevice { get; set; }

        public string? PublishedInf { get; set; }

        public string? CertThumbprint { get; set; }

        public string? Date { get; set; }
    }
}
