using System.Globalization;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using Katalog.Jadro.Model;
using Katalog.Jadro.Rozhrania;
using Microsoft.Win32.SafeHandles;

namespace Katalog.Skenovanie;

/// <summary>
/// Volumes of Windows and their identity: serial number and GUID of the volume, label, file system, size, the serial
/// number and bus of the physical disk (USB or internal), the address of a network share. Everything is read without
/// administrator rights; a part that cannot be read stays empty and the disk is still recognised by the others.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class ZvazkyWindows : IZvazky
{
    private const uint IoctlVolumeGetVolumeDiskExtents = 0x00560000;
    private const uint IoctlStorageQueryProperty = 0x002D1400;
    private const uint FileShareReadWrite = 0x00000003;
    private const uint OpenExisting = 3;
    private const int BusTypeUsb = 7;

    public IReadOnlyList<PripojenyZvazok> Pripojene()
    {
        var vysledok = new List<PripojenyZvazok>();
        foreach (var disk in DriveInfo.GetDrives())
        {
            try
            {
                if (!disk.IsReady)
                {
                    continue;
                }

                var koren = disk.RootDirectory.FullName;
                if (Identita(koren) is { } identita)
                {
                    vysledok.Add(new PripojenyZvazok(koren, identita, disk.AvailableFreeSpace));
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // a drive that vanished meanwhile
            }
        }

        return vysledok;
    }

    public IdentitaZvazku? Identita(string cesta)
    {
        var koren = KorenZvazku(cesta);
        if (koren is null || !CitajZvazok(koren, out var seriove, out var menovka, out var fs))
        {
            return null;
        }

        var (velkost, _) = Miesto(koren);
        var druh = DruhDisku.Nezisteny;
        string? adresa = null;
        string? guid = null;
        string? serioveDisku = null;

        if (koren.StartsWith(@"\\", StringComparison.Ordinal))
        {
            druh = DruhDisku.Sietovy;
            adresa = NormalizujAdresu(koren);
        }
        else
        {
            var pismeno = koren[..2];
            switch (new DriveInfo(koren).DriveType)
            {
                case DriveType.Network:
                    druh = DruhDisku.Sietovy;
                    adresa = SietovaAdresa(pismeno) is { } unc ? NormalizujAdresu(unc) : null;
                    break;
                case DriveType.CDRom:
                    druh = DruhDisku.Opticky;
                    break;
                case DriveType.Removable:
                    druh = DruhDisku.Usb;
                    break;
                case DriveType.Fixed:
                    druh = DruhDisku.Interny;
                    break;
            }

            if (druh != DruhDisku.Sietovy)
            {
                guid = GuidZvazku(koren);
                if (FyzickyDisk(pismeno) is { } cislo && PopisDisku(cislo) is { } popis)
                {
                    serioveDisku = popis.Seriove;
                    if (popis.Zbernica == BusTypeUsb && druh == DruhDisku.Interny)
                    {
                        // a USB disk often reports itself as fixed; the bus tells the truth
                        druh = DruhDisku.Usb;
                    }
                }
            }
        }

        return new IdentitaZvazku(druh, seriove, guid, serioveDisku, NullAkPrazdne(menovka), NullAkPrazdne(fs), velkost, adresa);
    }

    public (long? Velkost, long? Volne) Miesto(string cesta)
    {
        var koren = KorenZvazku(cesta);
        return koren is not null && GetDiskFreeSpaceExW(koren, out var volne, out var celkom, out _) ? ((long)celkom, (long)volne) : (null, null);
    }

    /// <summary>"\\NAS\Filmy\" → "\\nas\filmy" (shares are compared without regard to case).</summary>
    public static string NormalizujAdresu(string unc) => unc.TrimEnd('\\', '/').ToLowerInvariant();

    private static string? KorenZvazku(string cesta)
    {
        try
        {
            var koren = Path.GetPathRoot(Path.GetFullPath(cesta));
            if (string.IsNullOrEmpty(koren))
            {
                return null;
            }

            return koren.EndsWith('\\') ? koren : koren + "\\";
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }
    }

    private static bool CitajZvazok(string koren, out string? seriove, out string menovka, out string fs)
    {
        var bufMenovka = new char[261];
        var bufFs = new char[261];
        if (!GetVolumeInformationW(koren, bufMenovka, bufMenovka.Length, out var cislo, out _, out _, bufFs, bufFs.Length))
        {
            seriove = null;
            menovka = "";
            fs = "";
            return false;
        }

        seriove = cislo.ToString("X8", CultureInfo.InvariantCulture).Insert(4, "-");
        menovka = new string(bufMenovka).TrimEnd('\0');
        fs = new string(bufFs).TrimEnd('\0');
        return true;
    }

    private static string? GuidZvazku(string koren)
    {
        var buf = new char[64];
        return GetVolumeNameForVolumeMountPointW(koren, buf, buf.Length) ? new string(buf).TrimEnd('\0') : null;
    }

    private static string? SietovaAdresa(string pismeno)
    {
        var dlzka = 512;
        var buf = new char[dlzka];
        return WNetGetConnectionW(pismeno, buf, ref dlzka) == 0 ? new string(buf).TrimEnd('\0') : null;
    }

    /// <summary>Number of the physical disk holding the volume (\\.\PhysicalDriveN), when the volume lies on one disk.</summary>
    private static int? FyzickyDisk(string pismeno)
    {
        using var zvazok = CreateFileW(@"\\.\" + pismeno, 0, FileShareReadWrite, IntPtr.Zero, OpenExisting, 0, IntPtr.Zero);
        if (zvazok.IsInvalid)
        {
            return null;
        }

        // VOLUME_DISK_EXTENTS: NumberOfDiskExtents (4), padding (4), then DISK_EXTENT { DiskNumber (4), padding (4), ... }
        var buf = new byte[256];
        if (!DeviceIoControl(zvazok, IoctlVolumeGetVolumeDiskExtents, IntPtr.Zero, 0, buf, buf.Length, out _, IntPtr.Zero))
        {
            return null;
        }

        return BitConverter.ToInt32(buf, 0) == 1 ? BitConverter.ToInt32(buf, 8) : null;
    }

    /// <summary>Serial number and bus type of a physical disk (STORAGE_DEVICE_DESCRIPTOR).</summary>
    private static (string? Seriove, int Zbernica)? PopisDisku(int cislo)
    {
        using var disk = CreateFileW(@"\\.\PhysicalDrive" + cislo.ToString(CultureInfo.InvariantCulture), 0, FileShareReadWrite, IntPtr.Zero, OpenExisting, 0, IntPtr.Zero);
        if (disk.IsInvalid)
        {
            return null;
        }

        // STORAGE_PROPERTY_QUERY { PropertyId = StorageDeviceProperty (0), QueryType = PropertyStandardQuery (0), AdditionalParameters[1] }
        var dotaz = new byte[12];
        var buf = new byte[1024];
        var dotazHandle = GCHandle.Alloc(dotaz, GCHandleType.Pinned);
        try
        {
            if (!DeviceIoControl(disk, IoctlStorageQueryProperty, dotazHandle.AddrOfPinnedObject(), dotaz.Length, buf, buf.Length, out var precitane, IntPtr.Zero) ||
                precitane < 32)
            {
                return null;
            }
        }
        finally
        {
            dotazHandle.Free();
        }

        var zbernica = BitConverter.ToInt32(buf, 28);
        var odsadenie = BitConverter.ToInt32(buf, 24);
        string? seriove = null;
        if (odsadenie > 0 && odsadenie < buf.Length)
        {
            var koniec = Array.IndexOf(buf, (byte)0, odsadenie);
            seriove = NullAkPrazdne(Encoding.ASCII.GetString(buf, odsadenie, (koniec < 0 ? buf.Length : koniec) - odsadenie).Trim());
        }

        return (seriove, zbernica);
    }

    private static string? NullAkPrazdne(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetVolumeInformationW(string koren, char[] menovka, int dlzkaMenovky, out uint seriove, out uint maxDlzka, out uint priznaky, char[] fs, int dlzkaFs);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetVolumeNameForVolumeMountPointW(string koren, char[] nazov, int dlzka);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetDiskFreeSpaceExW(string koren, out ulong volneProVolajuceho, out ulong celkom, out ulong volneCelkom);

    [DllImport("mpr.dll", CharSet = CharSet.Unicode)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern int WNetGetConnectionW(string miestny, char[] vzdialeny, ref int dlzka);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern SafeFileHandle CreateFileW(string nazov, uint pristup, uint zdielanie, IntPtr bezpecnost, uint vytvorenie, uint priznaky, IntPtr sablona);

    [DllImport("kernel32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeviceIoControl(SafeFileHandle zariadenie, uint kod, IntPtr vstup, int dlzkaVstupu, byte[] vystup, int dlzkaVystupu, out int vratene, IntPtr prekryvanie);
}
