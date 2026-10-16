namespace XrealScreen.Core.Devices;

/// <summary>
/// Glasses models. Numeric values mirror the XREAL SDK's device-type enum so logs are
/// comparable (facts only, see docs/findings/xreal-sdk.md). Do not renumber.
/// </summary>
public enum GlassesModel
{
    Unknown = 0,
    Light = 1,
    Air = 2,
    Air2Pro = 3,
    Air2 = 4,
    Air2Ultra = 5,
    One = 10,
    OnePro = 11,
    XReal1S = 15,
}

public enum GlassesTransport
{
    /// <summary>IMU over USB HID (Air series). Not implemented yet.</summary>
    Hid,

    /// <summary>IMU/control over the USB network adapter (One series, 1S assumed).</summary>
    Network,
}

public sealed record GlassesProduct(int ProductId, GlassesModel Model, GlassesTransport Transport, string Source);

public static class GlassesCatalog
{
    public const int XrealVendorId = 0x3318;

    /// <summary>
    /// Known product IDs. "Source" states where the mapping comes from; entries tagged
    /// [from-GPL:facts-only] are facts taken from XRLinuxDriver, no code.
    /// </summary>
    public static IReadOnlyList<GlassesProduct> Products { get; } =
    [
        new(0x0435, GlassesModel.OnePro, GlassesTransport.Network, "[from-GPL:facts-only] XRLinuxDriver"),
        new(0x0436, GlassesModel.OnePro, GlassesTransport.Network, "[from-GPL:facts-only] XRLinuxDriver"),
        new(0x0437, GlassesModel.One, GlassesTransport.Network, "[from-GPL:facts-only] XRLinuxDriver"),
        new(0x0438, GlassesModel.One, GlassesTransport.Network, "[from-GPL:facts-only] XRLinuxDriver"),
        new(0x043D, GlassesModel.XReal1S, GlassesTransport.Network, "[from-GPL:facts-only] XRLinuxDriver; [hypothesis] transport"),
        new(0x043E, GlassesModel.XReal1S, GlassesTransport.Network, "[verified-hw] 1S fw 15.01.03.522"),
    ];

    /// <summary>EDID manufacturer ID used by XREAL glasses monitors.</summary>
    public const string XrealEdidManufacturer = "MRG";

    /// <summary>Known glasses monitors by EDID. Product code -1 = any product of that manufacturer.</summary>
    public static IReadOnlyList<GlassesDisplay> Displays { get; } =
    [
        new(XrealEdidManufacturer, 0x4101, GlassesModel.One, "[verified-hw] XREAL One PID 0x0438, EDID MRG4101, 3840×1080, 2026-10-16"),
        new(XrealEdidManufacturer, 0x4102, GlassesModel.XReal1S, "[verified-hw] 1S fw 15.01.03.522"),
        new(XrealEdidManufacturer, -1, GlassesModel.Unknown, "[hypothesis] other XREAL models use the same manufacturer ID"),
    ];

    public static GlassesProduct? Find(int vendorId, int productId) =>
        vendorId == XrealVendorId ? Products.FirstOrDefault(p => p.ProductId == productId) : null;

    public static GlassesDisplay? FindDisplay(string edidManufacturer, int edidProductCode) =>
        Displays.FirstOrDefault(d => d.EdidManufacturer == edidManufacturer && d.EdidProductCode == edidProductCode)
        ?? Displays.FirstOrDefault(d => d.EdidManufacturer == edidManufacturer && d.EdidProductCode < 0);
}

public sealed record GlassesDisplay(string EdidManufacturer, int EdidProductCode, GlassesModel Model, string Source);
