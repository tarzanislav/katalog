using System.Buffers.Binary;

// This file is compiled into Katalog.Jadro and, linked, into the helper Katalog.Zdravie (which depends on nothing):
// both read the same answers of the disks the same way. Only the base library may be used here.
namespace Katalog.Jadro.Zdravie;

/// <summary>One SMART attribute of an ATA/SATA disk: its normalised value, worst value, threshold and raw value.</summary>
public sealed record AtributSmart(int Id, int Hodnota, int Najhorsia, int Prah, long Surova);

/// <summary>
/// The health of one physical disk as the helper read it. Stav: 0 unknown, 1 good, 2 warning, 3 bad (as StavZdravia).
/// Chyba says why nothing could be read (an USB enclosure that does not pass SMART through).
/// </summary>
public sealed record VysledokZdravia(
    int Cislo, string? Model, string? Seriove, string Zbernica,
    int? Teplota, int? Hodiny, int? Realokovane, int? Cakajuce, int? Neopravitelne, int? Opotrebenie,
    int Stav, string? Dovod, string? Chyba);

/// <summary>What the helper writes: a version of the format and the disks.</summary>
public sealed record VystupZdravia(int Verzia, IReadOnlyList<VysledokZdravia> Disky);

/// <summary>
/// Reading SMART (stage E5): the attribute table of ATA/SATA disks and the health log of NVMe disks, judged by the same
/// rules – a failing attribute or a disk that says it is degraded is bad; reallocated, pending or unreadable sectors,
/// media errors or heat are a warning.
/// </summary>
public static class Smart
{
    public const int Neznamy = 0;
    public const int Dobry = 1;
    public const int Varovanie = 2;
    public const int Zly = 3;

    /// <summary>
    /// The attribute table (512 bytes of READ ATTRIBUTES: a version, then 30 entries of 12 bytes) with the thresholds
    /// (512 bytes of READ THRESHOLDS, the same layout; may be empty).
    /// </summary>
    public static IReadOnlyList<AtributSmart> CitajAta(ReadOnlySpan<byte> atributy, ReadOnlySpan<byte> prahy)
    {
        var vysledok = new List<AtributSmart>();
        for (var i = 0; i < 30; i++)
        {
            var od = 2 + (i * 12);
            if (od + 12 > atributy.Length)
            {
                break;
            }

            var id = atributy[od];
            if (id == 0)
            {
                continue;
            }

            long surova = 0;
            for (var b = 5; b >= 0; b--)
            {
                surova = (surova << 8) | atributy[od + 5 + b];
            }

            var prah = 0;
            for (var j = 0; j < 30 && 2 + (j * 12) + 2 <= prahy.Length; j++)
            {
                if (prahy[2 + (j * 12)] == id)
                {
                    prah = prahy[2 + (j * 12) + 1];
                    break;
                }
            }

            vysledok.Add(new AtributSmart(id, atributy[od + 3], atributy[od + 4], prah, surova));
        }

        return vysledok;
    }

    /// <summary>The health of an ATA/SATA disk from its attributes.</summary>
    public static VysledokZdravia VyhodnotAta(int cislo, string? model, string? seriove, string zbernica, IReadOnlyList<AtributSmart> atributy)
    {
        AtributSmart? A(int id) => atributy.FirstOrDefault(a => a.Id == id);
        int? Surova32(int id) => A(id) is { } a ? (int)Math.Min(a.Surova & 0xFFFFFFFF, int.MaxValue) : null;

        var hodiny = Surova32(9);
        var teplota = (A(194) ?? A(190)) is { } t && (t.Surova & 0xFF) is > 0 and < 120 and var c ? (int)c : (int?)null;
        var realokovane = Surova32(5);
        var cakajuce = Surova32(197);
        var neopravitelne = Surova32(198);
        // SSD wear: the normalised value of these attributes is the life left in percent
        var opotrebenie = new[] { 231, 233, 177, 202 }.Select(A).OfType<AtributSmart>().Where(a => a.Hodnota is > 0 and <= 100)
            .Select(a => (int?)(100 - a.Hodnota)).FirstOrDefault();

        var zlyhava = atributy.Where(a => a.Prah is > 0 and < 255 && a.Hodnota > 0 && a.Hodnota <= a.Prah).ToList();
        var dovody = new List<string>();
        int stav;
        if (zlyhava.Count > 0)
        {
            stav = Zly;
            dovody.Add("SMART hlási blížiace sa zlyhanie (atribút " + string.Join(", ", zlyhava.Select(a => a.Id)) + ")");
        }
        else
        {
            stav = Dobry;
        }

        if (realokovane > 0)
        {
            dovody.Add($"presunuté sektory: {realokovane}");
        }

        if (cakajuce > 0)
        {
            dovody.Add($"sektory čakajúce na presun: {cakajuce}");
        }

        if (neopravitelne > 0)
        {
            dovody.Add($"neopraviteľné sektory: {neopravitelne}");
        }

        if (teplota >= 55)
        {
            dovody.Add($"vysoká teplota {teplota} °C");
        }

        if (opotrebenie >= 90)
        {
            dovody.Add($"SSD opotrebované na {opotrebenie} %");
        }

        if (stav == Dobry && dovody.Count > 0)
        {
            stav = Varovanie;
        }

        // many bad sectors are no longer a warning
        if ((realokovane ?? 0) + (cakajuce ?? 0) + (neopravitelne ?? 0) >= 100)
        {
            stav = Zly;
        }

        return new VysledokZdravia(cislo, model, seriove, zbernica, teplota, hodiny, realokovane, cakajuce, neopravitelne, opotrebenie, stav,
            dovody.Count > 0 ? string.Join("; ", dovody) : null, null);
    }

    /// <summary>The health of an NVMe disk from its SMART / Health Information log page (512 bytes).</summary>
    public static VysledokZdravia VyhodnotNvme(int cislo, string? model, string? seriove, ReadOnlySpan<byte> log)
    {
        if (log.Length < 192)
        {
            return new VysledokZdravia(cislo, model, seriove, "NVMe", null, null, null, null, null, null, Neznamy, null, "Disk nevrátil záznam zdravia.");
        }

        var varovanie = log[0];
        var kelviny = BinaryPrimitives.ReadUInt16LittleEndian(log[1..]);
        int? teplota = kelviny is > 200 and < 400 ? kelviny - 273 : null;
        var rezerva = log[3];
        var prahRezervy = log[4];
        var opotrebenie = Math.Min((int)log[5], 255);
        var hodiny = (int)Math.Min(BinaryPrimitives.ReadUInt64LittleEndian(log[128..]), int.MaxValue);
        var chybyMedia = (int)Math.Min(BinaryPrimitives.ReadUInt64LittleEndian(log[160..]), int.MaxValue);

        var dovody = new List<string>();
        var stav = Dobry;
        if ((varovanie & 0x0D) != 0 || (prahRezervy > 0 && rezerva < prahRezervy))
        {
            stav = Zly;
            dovody.Add((varovanie & 0x08) != 0 ? "disk prešiel len na čítanie"
                : (varovanie & 0x04) != 0 ? "disk hlási zníženú spoľahlivosť"
                : "došla rezerva náhradných blokov");
        }

        if (opotrebenie >= 100)
        {
            stav = Zly;
            dovody.Add($"SSD opotrebované na {opotrebenie} %");
        }
        else if (opotrebenie >= 90)
        {
            dovody.Add($"SSD opotrebované na {opotrebenie} %");
        }

        if (chybyMedia > 0)
        {
            dovody.Add($"chyby média: {chybyMedia}");
        }

        if ((varovanie & 0x02) != 0 || teplota >= 70)
        {
            dovody.Add(teplota is { } t ? $"vysoká teplota {t} °C" : "vysoká teplota");
        }

        if (stav == Dobry && dovody.Count > 0)
        {
            stav = Varovanie;
        }

        return new VysledokZdravia(cislo, model, seriove, "NVMe", teplota, hodiny, null, null, chybyMedia, Math.Min(opotrebenie, 100), stav,
            dovody.Count > 0 ? string.Join("; ", dovody) : null, null);
    }

    /// <summary>The bus of a disk by STORAGE_BUS_TYPE.</summary>
    public static string Zbernica(int typ) => typ switch
    {
        3 => "ATA",
        7 => "USB",
        11 => "SATA",
        17 => "NVMe",
        1 => "SCSI",
        10 => "SAS",
        8 => "RAID",
        12 => "SD",
        _ => "iná",
    };
}
