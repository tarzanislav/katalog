using Katalog.Jadro.Rozhrania;

namespace Katalog.Jadro.Model;

/// <summary>A child of a folder as the library has it (before a scan).</summary>
public sealed record UlozenaPolozka(long Id, string Nazov, bool JePriecinok, long Velkost, long Zmeneny, bool Zmazana);

/// <summary>
/// What a scan changes in one folder: the found entries compared with the stored ones. Nothing is ever lost: a missing
/// entry is only marked as deleted, and a deleted one that appears again comes back with its history (its id, its
/// version and what was watched stay).
/// </summary>
public sealed record RozdielPriecinka(
    IReadOnlyList<PolozkaUloziska> NovePriecinky,
    IReadOnlyList<(long Id, string Nazov)> ExistujucePriecinky,
    IReadOnlyList<long> VratenePriecinky,
    IReadOnlyList<long> ZmazanePriecinky,
    IReadOnlyList<PolozkaUloziska> NoveSubory,
    IReadOnlyList<(long Id, PolozkaUloziska Polozka)> ZmeneneSubory,
    IReadOnlyList<(long Id, PolozkaUloziska Polozka)> VrateneSubory,
    IReadOnlyList<long> ZmazaneSubory)
{
    /// <summary>True when the folder's stored state needs no write.</summary>
    public bool JeBezZmeny =>
        NovePriecinky.Count == 0 && VratenePriecinky.Count == 0 && ZmazanePriecinky.Count == 0 &&
        NoveSubory.Count == 0 && ZmeneneSubory.Count == 0 && VrateneSubory.Count == 0 && ZmazaneSubory.Count == 0;

    /// <summary>
    /// Compares a folder's stored children with the entries found on the storage. A file is changed when its size or time
    /// of change differs (times are compared to the 100 ns tick, as NTFS keeps them). Names are compared exactly, as the
    /// library stores them.
    /// </summary>
    public static RozdielPriecinka Porovnaj(IReadOnlyList<UlozenaPolozka> ulozene, IReadOnlyList<PolozkaUloziska> najdene)
    {
        var ulozenePriecinky = new Dictionary<string, UlozenaPolozka>(StringComparer.Ordinal);
        var ulozeneSubory = new Dictionary<string, UlozenaPolozka>(StringComparer.Ordinal);
        foreach (var polozka in ulozene)
        {
            (polozka.JePriecinok ? ulozenePriecinky : ulozeneSubory)[polozka.Nazov] = polozka;
        }

        var novePriecinky = new List<PolozkaUloziska>();
        var existujuce = new List<(long, string)>();
        var vratenePriecinky = new List<long>();
        var noveSubory = new List<PolozkaUloziska>();
        var zmenene = new List<(long, PolozkaUloziska)>();
        var vratene = new List<(long, PolozkaUloziska)>();
        var videnePriecinky = new HashSet<string>(StringComparer.Ordinal);
        var videneSubory = new HashSet<string>(StringComparer.Ordinal);

        foreach (var polozka in najdene)
        {
            if (polozka.JePriecinok)
            {
                if (!videnePriecinky.Add(polozka.Nazov))
                {
                    continue;
                }

                if (ulozenePriecinky.TryGetValue(polozka.Nazov, out var stary))
                {
                    existujuce.Add((stary.Id, polozka.Nazov));
                    if (stary.Zmazana)
                    {
                        vratenePriecinky.Add(stary.Id);
                    }
                }
                else
                {
                    novePriecinky.Add(polozka);
                }

                continue;
            }

            if (!videneSubory.Add(polozka.Nazov))
            {
                continue;
            }

            if (!ulozeneSubory.TryGetValue(polozka.Nazov, out var ulozeny))
            {
                noveSubory.Add(polozka);
            }
            else if (ulozeny.Zmazana)
            {
                vratene.Add((ulozeny.Id, polozka));
            }
            else if (ulozeny.Velkost != polozka.Velkost || ulozeny.Zmeneny != polozka.Zmeneny)
            {
                zmenene.Add((ulozeny.Id, polozka));
            }
        }

        var zmazanePriecinky = ulozenePriecinky.Values.Where(p => !p.Zmazana && !videnePriecinky.Contains(p.Nazov)).Select(p => p.Id).ToList();
        var zmazaneSubory = ulozeneSubory.Values.Where(s => !s.Zmazana && !videneSubory.Contains(s.Nazov)).Select(s => s.Id).ToList();
        return new RozdielPriecinka(novePriecinky, existujuce, vratenePriecinky, zmazanePriecinky, noveSubory, zmenene, vratene, zmazaneSubory);
    }
}
