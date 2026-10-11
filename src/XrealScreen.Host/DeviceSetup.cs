using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;

namespace XrealScreen.Host;

/// <summary>A device node found by hardware ID.</summary>
internal sealed record DeviceInfo(string InstanceId, string? DriverVersion, string? InfPath);

/// <summary>SetupAPI / newdev interop for root-enumerated devices (setupapi.h, newdev.h; used by <see cref="DriverSetup"/>).</summary>
internal static partial class DeviceSetup
{
    private const int DigcfAllClasses = 0x4;
    private const int DicdGenerateId = 0x1;
    private const int SpdrpHardwareId = 0x1;
    private const int DifRegisterDevice = 0x19;
    private const int InstallFlagForce = 0x1;
    private const int SuoiForceDelete = 0x1;
    private const int ErrorNoMoreItems = 259;
    private const int ErrorInsufficientBuffer = 122;

    // DEVPKEY_Device_DriverVersion / DEVPKEY_Device_DriverInfPath (devpkey.h)
    private static readonly DEVPROPKEY DriverVersionKey = new(new Guid("a8b865dd-2e3d-4094-ad97-e593a70c75d6"), 3);
    private static readonly DEVPROPKEY DriverInfPathKey = new(new Guid("a8b865dd-2e3d-4094-ad97-e593a70c75d6"), 5);

    /// <summary>First device (present or not) whose hardware IDs contain <paramref name="hardwareId"/>.</summary>
    public static DeviceInfo? Find(string hardwareId)
    {
        DeviceInfo? found = null;
        ForEachDevice(hardwareId, (set, data) =>
        {
            found = new DeviceInfo(GetInstanceId(set, ref data), GetStringProperty(set, ref data, DriverVersionKey), GetStringProperty(set, ref data, DriverInfPathKey));
            return false;
        });
        return found;
    }

    /// <summary>Creates a root-enumerated device node with one hardware ID (what "devcon install" does before the driver).</summary>
    public static void CreateRootDevice(string hardwareId, Guid classGuid, string className, string description)
    {
        IntPtr set = SetupDiCreateDeviceInfoList(ref classGuid, IntPtr.Zero);
        ThrowIfInvalid(set, nameof(SetupDiCreateDeviceInfoList));
        try
        {
            var data = NewDevInfoData();
            Check(SetupDiCreateDeviceInfoW(set, className, ref classGuid, description, IntPtr.Zero, DicdGenerateId, ref data), nameof(SetupDiCreateDeviceInfoW));
            byte[] ids = Encoding.Unicode.GetBytes(hardwareId + "\0\0");
            Check(SetupDiSetDeviceRegistryPropertyW(set, ref data, SpdrpHardwareId, ids, ids.Length), nameof(SetupDiSetDeviceRegistryPropertyW));
            Check(SetupDiCallClassInstaller(DifRegisterDevice, set, ref data), "SetupDiCallClassInstaller(DIF_REGISTERDEVICE)");
        }
        finally
        {
            SetupDiDestroyDeviceInfoList(set);
        }
    }

    /// <summary>Installs the INF on all devices with the hardware ID (forced). Returns true when Windows needs a restart.</summary>
    public static bool UpdateDriver(string hardwareId, string fullInfPath)
    {
        Check(UpdateDriverForPlugAndPlayDevicesW(IntPtr.Zero, hardwareId, fullInfPath, InstallFlagForce, out bool reboot), nameof(UpdateDriverForPlugAndPlayDevicesW));
        return reboot;
    }

    /// <summary>Uninstalls every device node with the hardware ID.</summary>
    public static void RemoveDevices(string hardwareId) =>
        ForEachDevice(hardwareId, (set, data) =>
        {
            Check(DiUninstallDevice(IntPtr.Zero, set, ref data, 0, out _), nameof(DiUninstallDevice));
            return true;
        });

    /// <summary>Deletes a published driver package (oemNN.inf) from the driver store.</summary>
    public static void DeleteDriverPackage(string publishedInf) =>
        Check(SetupUninstallOEMInfW(Path.GetFileName(publishedInf), SuoiForceDelete, IntPtr.Zero), nameof(SetupUninstallOEMInfW));

    private static void ForEachDevice(string hardwareId, Func<IntPtr, SP_DEVINFO_DATA, bool> visit)
    {
        IntPtr set = SetupDiGetClassDevsW(IntPtr.Zero, null, IntPtr.Zero, DigcfAllClasses);
        ThrowIfInvalid(set, nameof(SetupDiGetClassDevsW));
        try
        {
            var data = NewDevInfoData();
            for (int i = 0; SetupDiEnumDeviceInfo(set, i, ref data); i++)
            {
                if (GetHardwareIds(set, ref data).Contains(hardwareId, StringComparer.OrdinalIgnoreCase) && !visit(set, data))
                {
                    return;
                }
            }

            int error = Marshal.GetLastPInvokeError();
            if (error != ErrorNoMoreItems)
            {
                throw new Win32Exception(error, nameof(SetupDiEnumDeviceInfo));
            }
        }
        finally
        {
            SetupDiDestroyDeviceInfoList(set);
        }
    }

    private static string[] GetHardwareIds(IntPtr set, ref SP_DEVINFO_DATA data)
    {
        SetupDiGetDeviceRegistryPropertyW(set, ref data, SpdrpHardwareId, out _, null, 0, out int size);
        if (size <= 0)
        {
            return [];
        }

        byte[] buffer = new byte[size];
        if (!SetupDiGetDeviceRegistryPropertyW(set, ref data, SpdrpHardwareId, out _, buffer, size, out _))
        {
            return [];
        }

        return Encoding.Unicode.GetString(buffer).Split('\0', StringSplitOptions.RemoveEmptyEntries);
    }

    private static string? GetStringProperty(IntPtr set, ref SP_DEVINFO_DATA data, DEVPROPKEY key)
    {
        SetupDiGetDevicePropertyW(set, ref data, ref key, out _, null, 0, out int size, 0);
        if (size <= 0)
        {
            return null;
        }

        byte[] buffer = new byte[size];
        return SetupDiGetDevicePropertyW(set, ref data, ref key, out _, buffer, size, out _, 0)
            ? Encoding.Unicode.GetString(buffer).TrimEnd('\0')
            : null;
    }

    private static unsafe string GetInstanceId(IntPtr set, ref SP_DEVINFO_DATA data)
    {
        const int Size = 512;
        char* buffer = stackalloc char[Size];
        Check(SetupDiGetDeviceInstanceIdW(set, ref data, buffer, Size, out int required), nameof(SetupDiGetDeviceInstanceIdW));
        return new string(buffer, 0, Math.Max(0, required - 1));
    }

    private static SP_DEVINFO_DATA NewDevInfoData() => new() { cbSize = Marshal.SizeOf<SP_DEVINFO_DATA>() };

    private static void Check(bool ok, string function)
    {
        if (!ok)
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError(), function);
        }
    }

    private static void ThrowIfInvalid(IntPtr handle, string function)
    {
        if (handle == new IntPtr(-1))
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError(), function);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SP_DEVINFO_DATA
    {
        public int cbSize;
        public Guid ClassGuid;
        public int DevInst;
        public IntPtr Reserved;
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct DEVPROPKEY(Guid fmtid, int pid)
    {
        public readonly Guid Fmtid = fmtid;
        public readonly int Pid = pid;
    }

    [LibraryImport("setupapi.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    private static partial IntPtr SetupDiGetClassDevsW(IntPtr classGuid, string? enumerator, IntPtr hwndParent, int flags);

    [LibraryImport("setupapi.dll", SetLastError = true)]
    private static partial IntPtr SetupDiCreateDeviceInfoList(ref Guid classGuid, IntPtr hwndParent);

    [LibraryImport("setupapi.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetupDiCreateDeviceInfoW(IntPtr set, string deviceName, ref Guid classGuid, string description, IntPtr hwndParent, int flags, ref SP_DEVINFO_DATA data);

    [LibraryImport("setupapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetupDiEnumDeviceInfo(IntPtr set, int index, ref SP_DEVINFO_DATA data);

    [LibraryImport("setupapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetupDiGetDeviceRegistryPropertyW(IntPtr set, ref SP_DEVINFO_DATA data, int property, out int regType, byte[]? buffer, int size, out int requiredSize);

    [LibraryImport("setupapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetupDiSetDeviceRegistryPropertyW(IntPtr set, ref SP_DEVINFO_DATA data, int property, byte[] buffer, int size);

    [LibraryImport("setupapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetupDiGetDevicePropertyW(IntPtr set, ref SP_DEVINFO_DATA data, ref DEVPROPKEY key, out int propertyType, byte[]? buffer, int size, out int requiredSize, int flags);

    [LibraryImport("setupapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static unsafe partial bool SetupDiGetDeviceInstanceIdW(IntPtr set, ref SP_DEVINFO_DATA data, char* instanceId, int size, out int requiredSize);

    [LibraryImport("setupapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetupDiCallClassInstaller(int installFunction, IntPtr set, ref SP_DEVINFO_DATA data);

    [LibraryImport("setupapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetupDiDestroyDeviceInfoList(IntPtr set);

    [LibraryImport("setupapi.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetupUninstallOEMInfW(string infFileName, int flags, IntPtr reserved);

    [LibraryImport("newdev.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool UpdateDriverForPlugAndPlayDevicesW(IntPtr hwndParent, string hardwareId, string fullInfPath, int installFlags, [MarshalAs(UnmanagedType.Bool)] out bool rebootRequired);

    [LibraryImport("newdev.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DiUninstallDevice(IntPtr hwndParent, IntPtr set, ref SP_DEVINFO_DATA data, int flags, [MarshalAs(UnmanagedType.Bool)] out bool needReboot);
}
