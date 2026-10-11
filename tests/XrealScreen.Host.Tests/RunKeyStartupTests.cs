using Microsoft.Win32;

namespace XrealScreen.Host.Tests;

public sealed class RunKeyStartupTests : IDisposable
{
    private readonly string _scratch = @"Software\XrealScreen.Tests\" + Guid.NewGuid().ToString("N");
    private readonly RegistryKey _root;

    public RunKeyStartupTests() => _root = Registry.CurrentUser.CreateSubKey(_scratch, writable: true);

    public void Dispose()
    {
        _root.Dispose();
        Registry.CurrentUser.DeleteSubKeyTree(_scratch, throwOnMissingSubKey: false);
    }

    private RunKeyStartup NewStartup(string command = "\"C:\\XrealScreen\\XrealScreen.App.exe\" --autostart") =>
        new("XrealScreen", command, _root, "Run", "Approved");

    [Fact]
    public void State_IsDisabled_WhenNoValue() => Assert.Equal(StartupState.Disabled, NewStartup().State);

    [Fact]
    public void EnableDisable_WritesAndRemovesRunValue()
    {
        var startup = NewStartup();

        Assert.Equal(StartupState.Enabled, startup.Enable());
        using (var run = _root.OpenSubKey("Run"))
        {
            Assert.Equal(startup.Command, run!.GetValue("XrealScreen"));
        }

        Assert.Equal(StartupState.Disabled, startup.Disable());
        Assert.Equal(StartupState.Disabled, startup.Disable()); // idempotent
    }

    [Fact]
    public void Enable_RefreshesStaleCommand()
    {
        NewStartup("\"C:\\old\\XrealScreen.App.exe\" --autostart").Enable();
        var startup = NewStartup();
        startup.Enable();

        using var run = _root.OpenSubKey("Run");
        Assert.Equal(startup.Command, run!.GetValue("XrealScreen"));
    }

    [Theory]
    [InlineData((byte)0x02, StartupState.Enabled)]
    [InlineData((byte)0x06, StartupState.Enabled)]
    [InlineData((byte)0x03, StartupState.DisabledByUser)]
    [InlineData((byte)0x07, StartupState.DisabledByUser)]
    public void State_HonoursTaskManagerSwitch(byte flag, StartupState expected)
    {
        var startup = NewStartup();
        startup.Enable();
        using (var approved = _root.CreateSubKey("Approved"))
        {
            approved.SetValue("XrealScreen", new byte[] { flag, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 }, RegistryValueKind.Binary);
        }

        Assert.Equal(expected, startup.State);
    }
}
