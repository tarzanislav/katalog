using System.Globalization;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using Katalog.Jadro.Zdravie;
using Microsoft.Win32.SafeHandles;

namespace Katalog.Zdravie;

/// <summary>Reads SMART of \\.\PhysicalDrive0 … 31 through DeviceIoControl (no WMI, no other program).</summary>
[SupportedOSPlatform("windows")]
internal static class CitanieDiskov
{
    private const uint GenericRead = 0x80000000;
    private const uint GenericWrite = 0x40000000;
    private const uint ZdielanieCitanieZapis = 3;
    private const uint Existujuci = 3;
    private const uint IoctlStorageQueryProperty = 0x002D1400;
    private const uint SmartRcvDriveData = 0x0007C088;
    private const byte CitajAtributy = 0xD0;
    private const byte CitajPrahy = 0xD1;

    public static IReadOnlyList<VysledokZdravia> Vsetky()
    {
        var vysledok = new List<VysledokZdravia>();
        for (var cislo = 0; cislo < 32; cislo++)
        {
            using var disk = CreateFileW(@"\\.\PhysicalDrive" + cislo.ToString(CultureInfo.InvariantCulture), GenericRead | GenericWrite, ZdielanieCitanieZapis,
                IntPtr.Zero, Existujuci, 0, IntPtr.Zero);
            if (disk.IsInvalid)
            {
                continue;
            }

            try
            {
                vysledok.Add(Precitaj(disk, cislo));
            }
            catch (Exception ex) when (ex is ArgumentException or IndexOutOfRangeException)
            {
                vysledok.Add(new VysledokZdravia(cislo, null, null, "iná", null, null, null, null, null, null, Smart.Neznamy, null, "Odpoveď disku sa nedala prečítať."));
            }
        }

        return vysledok;
    }

    private static VysledokZdravia Precitaj(SafeFileHandle disk, int cislo)
    {
        var (model, seriove, zbernica) = Popis(disk);
        var nazovZbernice = Smart.Zbernica(zbernica);
        if (zbernica == 17)
        {
            return ZdravieNvme(disk) is { } log
                ? Smart.VyhodnotNvme(cislo, model, seriove, log)
                : new VysledokZdravia(cislo, model, seriove, nazovZbernice, null, null, null, null, null, null, Smart.Neznamy, null, "Disk NVMe nevrátil záznam zdravia.");
        }

        var atributy = SmartAta(disk, cislo, CitajAtributy);
        if (atributy is null)
        {
            return new VysledokZdravia(cislo, model, seriove, nazovZbernice, null, null, null, null, null, null, Smart.Neznamy, null,
                zbernica == 7 ? "Rámček USB disku SMART neposiela ďalej." : "Disk nevrátil údaje SMART.");
        }

        var prahy = SmartAta(disk, cislo, CitajPrahy) ?? [];
        return Smart.VyhodnotAta(cislo, model, seriove, nazovZbernice, Smart.CitajAta(atributy, prahy));
    }

    /// <summary>Model, serial number and bus (STORAGE_DEVICE_DESCRIPTOR, as the scan reads the serial).</summary>
    private static (string? Model, string? Seriove, int Zbernica) Popis(SafeFileHandle disk)
    {
        var dotaz = new byte[12];
        var buf = new byte[1024];
        if (!Volaj(disk, IoctlStorageQueryProperty, dotaz, buf, out var precitane) || precitane < 32)
        {
            return (null, null, 0);
        }

        string? Text(int odsadenie)
        {
            if (odsadenie <= 0 || odsadenie >= precitane)
            {
                return null;
            }

            var koniec = Array.IndexOf(buf, (byte)0, odsadenie);
            var text = Encoding.ASCII.GetString(buf, odsadenie, (koniec < 0 ? precitane : koniec) - odsadenie).Trim();
            return text.Length == 0 ? null : text;
        }

        var model = string.Join(" ", new[] { Text(BitConverter.ToInt32(buf, 12)), Text(BitConverter.ToInt32(buf, 16)) }.Where(t => t is not null));
        return (model.Length == 0 ? null : model, Text(BitConverter.ToInt32(buf, 24)), BitConverter.ToInt32(buf, 28));
    }

    /// <summary>The NVMe SMART / Health Information log page (log 02h, 512 bytes).</summary>
    private static byte[]? ZdravieNvme(SafeFileHandle disk)
    {
        // STORAGE_PROPERTY_QUERY { StorageDeviceProtocolSpecificProperty (50), PropertyStandardQuery (0) } followed by
        // STORAGE_PROTOCOL_SPECIFIC_DATA { ProtocolTypeNvme (3), NVMeDataTypeLogPage (2), log 2, sub 0, offset 40, length 512, … }
        var dotaz = new byte[8 + 40 + 512];
        BitConverter.GetBytes(50).CopyTo(dotaz, 0);
        BitConverter.GetBytes(3).CopyTo(dotaz, 8);
        BitConverter.GetBytes(2).CopyTo(dotaz, 12);
        BitConverter.GetBytes(2).CopyTo(dotaz, 16);
        BitConverter.GetBytes(40).CopyTo(dotaz, 24);
        BitConverter.GetBytes(512).CopyTo(dotaz, 28);
        var vystup = new byte[dotaz.Length];
        if (!Volaj(disk, IoctlStorageQueryProperty, dotaz, vystup, out var precitane) || precitane < 8 + 40 + 192)
        {
            return null;
        }

        // STORAGE_PROTOCOL_DATA_DESCRIPTOR: Version, Size, then the protocol data header; the log at its offset
        var odsadenie = BitConverter.ToInt32(vystup, 8 + 16);
        var dlzka = BitConverter.ToInt32(vystup, 8 + 20);
        return odsadenie > 0 && dlzka >= 192 && 8 + odsadenie + dlzka <= precitane ? vystup.AsSpan(8 + odsadenie, Math.Min(dlzka, 512)).ToArray() : null;
    }

    /// <summary>READ ATTRIBUTES or READ THRESHOLDS of an ATA disk (SMART_RCV_DRIVE_DATA), 512 bytes; null when the disk does not answer.</summary>
    private static byte[]? SmartAta(SafeFileHandle disk, int cislo, byte prikaz)
    {
        // SENDCMDINPARAMS (packed): cBufferSize (4), IDEREGS (8), bDriveNumber (1), reserved (3 + 16), bBuffer
        var dotaz = new byte[32 + 1];
        BitConverter.GetBytes(512).CopyTo(dotaz, 0);
        dotaz[4] = prikaz;             // features
        dotaz[5] = 1;                  // sector count
        dotaz[6] = 1;                  // sector number
        dotaz[7] = 0x4F;               // cylinder low  (SMART signature)
        dotaz[8] = 0xC2;               // cylinder high (SMART signature)
        dotaz[9] = (byte)(0xA0 | ((cislo & 1) << 4));
        dotaz[10] = 0xB0;              // SMART command
        dotaz[12] = (byte)cislo;
        var vystup = new byte[16 + 512];
        if (!Volaj(disk, SmartRcvDriveData, dotaz, vystup, out var precitane) || precitane < 16 + 362 || vystup[4] != 0)
        {
            return null;
        }

        // SENDCMDOUTPARAMS: cBufferSize (4), DRIVERSTATUS (12), bBuffer (512)
        return vystup.AsSpan(16, 512).ToArray();
    }

    private static bool Volaj(SafeFileHandle disk, uint kod, byte[] vstup, byte[] vystup, out int precitane)
    {
        var handle = GCHandle.Alloc(vstup, GCHandleType.Pinned);
        try
        {
            return DeviceIoControl(disk, kod, handle.AddrOfPinnedObject(), vstup.Length, vystup, vystup.Length, out precitane, IntPtr.Zero);
        }
        finally
        {
            handle.Free();
        }
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern SafeFileHandle CreateFileW(string nazov, uint pristup, uint zdielanie, IntPtr bezpecnost, uint vytvorenie, uint priznaky, IntPtr sablona);

    [DllImport("kernel32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeviceIoControl(SafeFileHandle zariadenie, uint kod, IntPtr vstup, int dlzkaVstupu, byte[] vystup, int dlzkaVystupu, out int vratene, IntPtr prekryvanie);
}
