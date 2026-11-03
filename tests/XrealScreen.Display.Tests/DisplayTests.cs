using XrealScreen.Core.Abstractions;
using XrealScreen.Core.Devices;
using XrealScreen.Core.Workspace;
using XrealScreen.Display;
using XrealScreen.Display.Native;

namespace XrealScreen.Display.Tests;

public class DisplayTests
{
    [Fact]
    public unsafe void NativeStructSizes_MatchWingdi()
    {
        Assert.Equal(8, sizeof(LUID));
        Assert.Equal(20, sizeof(DISPLAYCONFIG_PATH_SOURCE_INFO));
        Assert.Equal(48, sizeof(DISPLAYCONFIG_PATH_TARGET_INFO));
        Assert.Equal(72, sizeof(DISPLAYCONFIG_PATH_INFO));
        Assert.Equal(64, sizeof(DISPLAYCONFIG_MODE_INFO));
        Assert.Equal(20, sizeof(DISPLAYCONFIG_DEVICE_INFO_HEADER));
        Assert.Equal(84, sizeof(DISPLAYCONFIG_SOURCE_DEVICE_NAME));
        Assert.Equal(420, sizeof(DISPLAYCONFIG_TARGET_DEVICE_NAME));
        Assert.Equal(220, sizeof(DEVMODEW));
    }

    [Theory]
    [InlineData("MRG")]
    [InlineData("MSI")]
    [InlineData("AAA")]
    [InlineData("ZZZ")]
    public void EdidManufacturer_RoundTrips(string id) =>
        Assert.Equal(id, EdidIds.DecodeManufacturer(EdidIds.EncodeManufacturer(id)));

    [Fact]
    public void EdidManufacturer_DecodesKnownWord()
    {
        // "MSI": M=13, S=19, I=9 → 0b0_01101_10011_01001 = 0x3669, stored byte-swapped by CCD.
        Assert.Equal("MSI", EdidIds.DecodeManufacturer(0x6936));
    }

    [Fact]
    public void Locator_FindsXreal1SByEdid()
    {
        var monitors = new[]
        {
            Monitor("MSI", 0x3FA6, @"\\.\DISPLAY1"),
            Monitor("MRG", 0x4102, @"\\.\DISPLAY3"),
        };

        var glasses = GlassesDisplayLocator.FindGlasses(monitors);
        Assert.Equal(@"\\.\DISPLAY3", glasses?.GdiDeviceName);
        Assert.Equal(GlassesModel.XReal1S, GlassesDisplayLocator.Identify(glasses!));
        Assert.Equal("MRG4102", glasses!.EdidId);
    }

    [Fact]
    public void Locator_FindsXrealOneByEdid() =>
        Assert.Equal(GlassesModel.One, GlassesDisplayLocator.Identify(Monitor("MRG", 0x4101, @"\\.\DISPLAY3")));

    [Fact]
    public void Locator_UnknownXrealProduct_FallsBackToManufacturer() =>
        Assert.Equal(GlassesModel.Unknown, GlassesDisplayLocator.Identify(Monitor("MRG", 0x9999, @"\\.\DISPLAY9")));

    [Fact]
    public void ReadingMonitor_UsesTheDeskAndSkipsTheGlasses()
    {
        DisplayMonitor desk = Screen("BNQ", 0x1234, @"\\.\DISPLAY2", primary: true, 3840, 2160);
        DisplayMonitor glasses = Screen("MRG", 0x4101, @"\\.\DISPLAY3", primary: false, 3840, 1080);
        DisplayMonitor phantom = Screen("CHY", 0x0001, @"\\.\DISPLAY9", primary: false, 1920, 1080);

        Assert.Equal(@"\\.\DISPLAY2", ReadingMonitor.Choose([desk, glasses, phantom])?.GdiDeviceName);
    }

    [Fact]
    public void ReadingMonitor_SkipsTheGlassesWhenTheyArePrimary()
    {
        DisplayMonitor glasses = Screen("MRG", 0x4101, @"\\.\DISPLAY3", primary: true, 3840, 1080);
        DisplayMonitor laptop = Screen("LGD", 0x0001, @"\\.\DISPLAY1", primary: false, 1920, 1200);

        Assert.Equal(@"\\.\DISPLAY1", ReadingMonitor.Choose([glasses, laptop])?.GdiDeviceName);
        Assert.Null(ReadingMonitor.Choose([glasses]));
    }

    private static DisplayMonitor Screen(string manufacturer, int product, string gdi, bool primary, int width, int height) =>
        new(gdi, "x", manufacturer, product, "path-" + gdi, new Resolution(width, height), 0, 0, 60, primary, "DisplayPort", 1, 1);

    private static DisplayMonitor Monitor(string manufacturer, int product, string gdi) =>
        new(gdi, "x", manufacturer, product, "path", new Resolution(1920, 1080), 0, 0, 60, true, "DisplayPort", 1, 1);
}
