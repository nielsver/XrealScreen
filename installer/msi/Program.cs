using FalkForge;
using FalkForge.Builders;
using FalkForge.Compiler.Msi;
using FalkForge.Models;

// XrealScreen.msi (ADR-0013): per-machine, unpackaged self-contained app in Program Files, optional virtual display
// driver feature installed by "xrs driver" (DriverSetup). Arguments (from installer\Build-Msi.ps1):
//   --payload <dir>   publish folder (XrealScreen.App.exe, xrs.exe, optional driver\)
//   --version <x.y.z> product version (MSI compares major.minor.build)
//   -o <file.msi>     output
// Signing (later, ADR-0013): set XRS_SIGN_THUMBPRINT (cert in CurrentUser\My) to sign the MSI.

string payload = Path.GetFullPath(Arg("--payload") ?? throw new ArgumentException("--payload <publish folder> is required"));
var version = Version.Parse(Arg("--version") ?? "0.1.0");
bool hasDriver = File.Exists(Path.Combine(payload, "driver", "VirtualDisplayDriver.inf"));
string icon = Path.Combine(payload, "Assets", "AppIcon.ico");

var installDir = KnownFolder.ProgramFiles64 / "XrealScreen";

return Installer.Build(args, p =>
{
    p.Name = "XrealScreen";
    p.Manufacturer = "Niklas Rydén";
    p.Version = version;
    p.Description = "Head-tracked multi-monitor workspace for XREAL glasses";
    p.HelpUrl = "https://github.com/nikryden/Screen-XREAL-1S";
    p.AboutUrl = "https://github.com/nikryden/Screen-XREAL-1S";
    // Never change: Windows Installer finds older versions to upgrade by this code.
    p.UpgradeCode = new Guid("d322530b-9c76-4b91-882d-db8616d6ea57");
    p.Scope = InstallScope.PerMachine;
    p.Architecture = ProcessorArchitecture.X64;
    p.DefaultInstallDirectory = installDir;
    p.ProductIcon(icon);
    p.UseDialogSet(hasDriver ? MsiDialogSet.FeatureTree : MsiDialogSet.InstallDir);
    // The app usually runs in the tray: let Restart Manager close it (it restores the desktop on exit).
    p.EnableRestartManagerSupport();

    p.Require(MsiProperty.WindowsBuildNumber >= 22000, "XrealScreen needs Windows 11.");
    p.Require(Condition.Is64BitOS, "XrealScreen needs 64-bit Windows.");

    p.MajorUpgrade(mu => mu.Schedule(RemoveExistingProductsSchedule.AfterInstallValidate));
    p.Downgrade(d => d.Block("A newer version of XrealScreen is already installed."));

    p.Feature("App", f =>
    {
        f.Title = "XrealScreen";
        f.Description = "The XrealScreen app and the xrs command-line tool.";
        f.IsRequired = true;
        f.Files(files => files.FromDirectory(payload).To(installDir));
        f.Shortcut("XrealScreen", "XrealScreen.App.exe")
            .WithDescription("Head-tracked multi-monitor workspace for XREAL glasses")
            .OnStartMenu();
    });

    // "Start with Windows" writes HKCU\...\Run\XrealScreen (AppStartup); remove it for the installing user.
    p.RemoveRegistry(r => r
        .Id("RemoveRunValue")
        .Root(RegistryRoot.CurrentUser)
        .Key(@"Software\Microsoft\Windows\CurrentVersion\Run")
        .Name("XrealScreen")
        .RemoveValue());

    if (hasDriver)
    {
        AddDriverFeature(p);
    }

    if (Environment.GetEnvironmentVariable("XRS_SIGN_THUMBPRINT") is { Length: > 0 } thumbprint)
    {
        p.Signing(s => s.Thumbprint(thumbprint).Timestamp("http://timestamp.digicert.com"));
    }
}, new MsiCompiler());

// Optional feature: virtual display driver (virtual-display-rs, ADR-0008). Deferred, elevated custom actions run the
// installed xrs.exe; its DriverSetup keeps an existing driver and removes only what it installed.
// Silent: msiexec /i XrealScreen.msi /qn  (driver included) or  ADDLOCAL=App  (without driver).
static void AddDriverFeature(PackageBuilder p)
{
    p.Feature("Driver", f =>
    {
        f.Title = "Virtual display driver";
        f.Description = "virtual-display-rs (MIT), needed for virtual screens. Signed with a test certificate that is "
            + "trusted on this PC while the driver is installed. An existing virtual-display-rs driver is kept.";
        f.IsDefault = true;
    });

    // MSI expands [INSTALLDIR] when the deferred action is scheduled. No other square brackets in these scripts.
    const string Xrs = "& '[INSTALLDIR]xrs.exe'";
    p.CustomAction("XrsDriverInstall", ca =>
    {
        ca.PowerShellScript($"{Xrs} driver install --driver-dir '[INSTALLDIR]driver'; exit $LASTEXITCODE");
        ca.Deferred();
        ca.NoImpersonate();
    });
    p.CustomAction("XrsDriverInstallRollback", ca =>
    {
        ca.PowerShellScript($"{Xrs} driver uninstall; exit 0");
        ca.Rollback();
        ca.NoImpersonate();
    });
    p.CustomAction("XrsDriverUninstall", ca =>
    {
        ca.PowerShellScript($"{Xrs} driver uninstall; exit 0");
        ca.Deferred();
        ca.NoImpersonate();
        ca.ContinueOnError();
    });

    // &Feature = requested state, !Feature = installed state (3 = local, 2 = absent).
    // A major upgrade removes the old product first: keep the driver then (the new version keeps the existing device).
    p.ExecuteSequence(seq => seq
        .Action("XrsDriverInstallRollback")
            .After("InstallFiles")
            .Condition("&Driver=3 AND NOT !Driver=3")
        .Action("XrsDriverInstall")
            .After("XrsDriverInstallRollback")
            .Condition("&Driver=3 AND NOT !Driver=3")
        .Action("XrsDriverUninstall")
            .Before("RemoveFiles")
            .Condition("&Driver=2 AND !Driver=3 AND NOT UPGRADINGPRODUCTCODE"));
}

string? Arg(string name)
{
    int i = Array.IndexOf(args, name);
    return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
}
