using System.IO;
using Katalog.Data;
using Katalog.Jadro.Model;
using Katalog.Jadro.Rozhrania;
using Katalog.Skenovanie;

namespace Katalog.Okno;

/// <summary>
/// Which disks of the library are connected now and where. Refreshed at start, when Windows reports a connected or
/// removed volume, and after a disk is added. Network disks added by their address are checked too (with a time limit,
/// so an unreachable server never blocks the window).
/// </summary>
internal sealed class StavPripojenia(IZvazky zvazky)
{
    private Dictionary<long, NajdenyDisk> _pripojene = [];

    /// <summary>Disks connected for certain, by id.</summary>
    public IReadOnlyDictionary<long, NajdenyDisk> Pripojene => _pripojene;

    public IZvazky Zvazky => zvazky;

    /// <summary>
    /// Finds the connected disks; returns those that are new since the last refresh and those whose volume matched only
    /// uncertainly (the user confirms them).
    /// </summary>
    public async Task<(IReadOnlyList<NajdenyDisk> Nove, IReadOnlyList<NajdenyDisk> Neiste)> ObnovAsync(IReadOnlyList<Disk> disky)
    {
        var najdene = await Task.Run(() =>
        {
            var vysledok = RozpoznanieDiskov.Najdi(disky, zvazky.Pripojene()).ToList();
            // network disks added by their address (\\server\share) that are not mapped to a letter
            foreach (var disk in disky.Where(d => d.Druh == DruhDisku.Sietovy && d.Identita.Adresa is not null && vysledok.All(n => n.Disk.Id != d.Id)))
            {
                var adresa = disk.Identita.Adresa!;
                if (DostupneVCase(adresa, TimeSpan.FromSeconds(3)) && zvazky.Identita(adresa) is { } identita &&
                    ZhodaZvazku.Porovnaj(disk.Identita, identita).Verdikt == VerdiktZhody.Isto)
                {
                    var cesta = disk.Koren.Length == 0 ? adresa : Path.Combine(adresa, disk.Koren.Replace('/', '\\'));
                    vysledok.Add(new NajdenyDisk(disk, new PripojenyZvazok(adresa, identita, zvazky.Miesto(adresa).Volne), cesta, VerdiktZhody.Isto));
                }
            }

            return vysledok;
        }).ConfigureAwait(true);

        var iste = najdene.Where(n => n.Verdikt == VerdiktZhody.Isto).GroupBy(n => n.Disk.Id).ToDictionary(g => g.Key, g => g.First());
        var nove = iste.Values.Where(n => !_pripojene.ContainsKey(n.Disk.Id)).ToList();
        _pripojene = iste;
        var neiste = najdene.Where(n => n.Verdikt == VerdiktZhody.Opytat && !iste.ContainsKey(n.Disk.Id)).ToList();
        return (nove, neiste);
    }

    /// <summary>The user confirmed an uncertain match: the disk is connected there.</summary>
    public void Potvrd(NajdenyDisk najdeny) => _pripojene[najdeny.Disk.Id] = najdeny with { Verdikt = VerdiktZhody.Isto };

    public string? Cesta(long diskId) => _pripojene.TryGetValue(diskId, out var n) ? n.Cesta : null;

    /// <summary>A storage to scan the disk now, or null when it is not connected.</summary>
    public IUlozisko? Ulozisko(long diskId) =>
        _pripojene.TryGetValue(diskId, out var n) ? new LokalneUlozisko(n.Cesta, n.Disk.Druh, zvazky) : null;

    private static bool DostupneVCase(string cesta, TimeSpan limit)
    {
        var uloha = Task.Run(() => Directory.Exists(cesta));
        return uloha.Wait(limit) && uloha.Result;
    }
}
