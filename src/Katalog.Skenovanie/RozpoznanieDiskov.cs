using Katalog.Jadro.Model;

namespace Katalog.Skenovanie;

/// <summary>A disk of the library found among the connected volumes.</summary>
/// <param name="Cesta">Where its content is now ("E:\", "E:\Filmy", "\\nas\filmy").</param>
public sealed record NajdenyDisk(Disk Disk, PripojenyZvazok Zvazok, string Cesta, VerdiktZhody Verdikt);

/// <summary>
/// Which disks of the library are connected now: every connected volume is matched to the known disks by the identity of
/// the volume (never by its letter). A disk that is one folder of a volume is connected when that folder exists.
/// A volume matched only uncertainly is returned with <see cref="VerdiktZhody.Opytat"/>: the user confirms.
/// </summary>
public static class RozpoznanieDiskov
{
    public static IReadOnlyList<NajdenyDisk> Najdi(IReadOnlyList<Disk> disky, IReadOnlyList<PripojenyZvazok> zvazky, Func<string, bool>? existuje = null)
    {
        existuje ??= Directory.Exists;
        var vysledok = new List<NajdenyDisk>();
        foreach (var zvazok in zvazky)
        {
            // all disks of this volume: the whole volume and its folders are separate disks with the same identity
            var kandidati = disky
                .Select(d => (Disk: d, Zhoda: ZhodaZvazku.Porovnaj(d.Identita, zvazok.Identita)))
                .Where(k => k.Zhoda.Verdikt != VerdiktZhody.Iny)
                .ToList();
            if (kandidati.Count == 0)
            {
                continue;
            }

            var najlepsie = kandidati.Max(k => k.Zhoda.Body);
            foreach (var (disk, zhoda) in kandidati.Where(k => k.Zhoda.Body == najlepsie))
            {
                var cesta = disk.Koren.Length == 0 ? zvazok.Koren : Path.Combine(zvazok.Koren, disk.Koren.Replace('/', Path.DirectorySeparatorChar));
                if (disk.Koren.Length > 0 && !existuje(cesta))
                {
                    continue;
                }

                // two whole-volume disks with the same identity cannot both be this volume: the user decides
                var dvojznacne = disk.Koren.Length == 0 && kandidati.Count(k => k.Zhoda.Body == najlepsie && k.Disk.Koren.Length == 0) > 1;
                vysledok.Add(new NajdenyDisk(disk, zvazok, cesta, dvojznacne ? VerdiktZhody.Opytat : zhoda.Verdikt));
            }
        }

        return vysledok;
    }

    /// <summary>The path of a folder disk inside its volume: "E:\Filmy\Nové" on "E:\" → "Filmy/Nové".</summary>
    public static string RelativnyKoren(string korenZvazku, string cesta)
    {
        var relativna = Path.GetRelativePath(korenZvazku, cesta);
        return relativna == "." ? "" : relativna.Replace(Path.DirectorySeparatorChar, '/').Trim('/');
    }
}
