using Katalog.Data;
using Katalog.Jadro.Model;
using Katalog.Media;

namespace Katalog.Ulohy;

/// <summary>How a recognition of a disk ended: versions put into the library and files set aside as no titles.</summary>
public sealed record VysledokRozpoznania(int Verzie, int Mimo);

/// <summary>
/// Puts the videos of a disk into the library of titles (stage E4) only from the names the library already knows – the
/// disk need not be connected and nothing goes to the internet. Parts of one film (CD1, CD2) and a whole Blu-ray or DVD
/// structure (BDMV, VIDEO_TS) become one version; samples, trailers and extras are set aside.
/// </summary>
public static class RozpoznanieDisku
{
    private static readonly HashSet<string> Struktury = new(StringComparer.OrdinalIgnoreCase) { "BDMV", "VIDEO_TS" };

    /// <param name="nazovDisku">The name for a Blu-ray or DVD structure right at the root of the disk (an optical disc).</param>
    public static async Task<VysledokRozpoznania> SpustiAsync(Kniznica kniznica, long diskId, string nazovDisku, CancellationToken zrusenie)
    {
        IReadOnlyList<VideoNaZaradenie> videa;
        IReadOnlyDictionary<long, (long? Rodic, string Nazov)> priecinky;
        using (var c = kniznica.OtvorCitanie())
        {
            videa = Tituly.NaZaradenie(c, diskId, int.MaxValue);
            if (videa.Count == 0)
            {
                return new VysledokRozpoznania(0, 0);
            }

            priecinky = Tituly.PriecinkyDisku(c, diskId);
        }

        var (jednotky, mimo) = Zoskup(videa, priecinky, nazovDisku);
        zrusenie.ThrowIfCancellationRequested();
        var zapisy = new List<Task<long?>>(jednotky.Count);
        foreach (var jednotka in jednotky)
        {
            zapisy.Add(kniznica.Zapis.ZapisAsync(s => Tituly.Zarad(s, jednotka, Cas.Teraz())));
        }

        if (mimo.Count > 0)
        {
            await kniznica.Zapis.ZapisAsync(s => Tituly.MimoKniznice(s, mimo, pouzivatel: false)).ConfigureAwait(false);
        }

        var verzie = 0;
        foreach (var zapis in zapisy)
        {
            if (await zapis.ConfigureAwait(false) is not null)
            {
                verzie++;
            }
        }

        return new VysledokRozpoznania(verzie, mimo.Count);
    }

    /// <summary>The versions the videos make up, and the files that are no titles.</summary>
    internal static (IReadOnlyList<JednotkaKniznice> Jednotky, IReadOnlyList<long> Mimo) Zoskup(
        IReadOnlyList<VideoNaZaradenie> videa, IReadOnlyDictionary<long, (long? Rodic, string Nazov)> priecinky, string nazovDisku)
    {
        var cesty = new Dictionary<long, (IReadOnlyList<string> Mena, IReadOnlyList<long> Idcka)>();
        (IReadOnlyList<string> Mena, IReadOnlyList<long> Idcka) Cesta(long priecinokId)
        {
            if (cesty.TryGetValue(priecinokId, out var znama))
            {
                return znama;
            }

            var mena = new List<string>();
            var idcka = new List<long>();
            for (long? id = priecinokId; id is { } i && priecinky.TryGetValue(i, out var p) && mena.Count < 64; id = p.Rodic)
            {
                if (p.Rodic is null)
                {
                    break;
                }

                mena.Add(p.Nazov);
                idcka.Add(i);
            }

            mena.Reverse();
            idcka.Reverse();
            return cesty[priecinokId] = (mena, idcka);
        }

        var jednotky = new List<JednotkaKniznice>();
        var mimo = new List<long>();
        var struktury = new Dictionary<long, (NavrhTitulu? Navrh, List<long> Subory)>();
        var casti = new Dictionary<(long Priecinok, string Kluc, string? Doplnok), (NavrhTitulu Navrh, List<(int Cast, long Subor)> Subory)>();
        foreach (var video in videa)
        {
            var (mena, idcka) = Cesta(video.PriecinokId);
            var index = -1;
            for (var i = mena.Count - 1; i >= 0; i--)
            {
                if (Struktury.Contains(mena[i]))
                {
                    index = i;
                    break;
                }
            }

            if (index >= 0)
            {
                // the folder above BDMV or VIDEO_TS is the film (or the disc itself at the root)
                var kluc = index > 0 ? idcka[index - 1] : 0;
                if (!struktury.TryGetValue(kluc, out var struktura))
                {
                    var navrh = index > 0
                        ? RozpoznanieNazvu.Rozpoznaj(mena.Take(index - 1).ToList(), mena[index - 1], strukturaDisku: true)
                        : RozpoznanieNazvu.Rozpoznaj([], nazovDisku, strukturaDisku: true);
                    struktura = (navrh, []);
                    struktury[kluc] = struktura;
                }

                struktura.Subory.Add(video.SuborId);
                continue;
            }

            var n = RozpoznanieNazvu.Rozpoznaj(mena, video.Nazov);
            if (n is null || n.Extra || (n.Druh == DruhTitulu.Serial && n.Epizody.Count == 0))
            {
                mimo.Add(video.SuborId);
            }
            else if (n.Druh == DruhTitulu.Film && n.Cast is { } cast)
            {
                var kluc = (video.PriecinokId, n.Kluc, n.Doplnok);
                if (!casti.TryGetValue(kluc, out var film))
                {
                    casti[kluc] = film = (n with { Cast = null }, []);
                }

                film.Subory.Add((cast, video.SuborId));
            }
            else
            {
                jednotky.Add(new JednotkaKniznice(n, [video.SuborId]));
            }
        }

        // the parts of a film are one version, in the order of their numbers
        foreach (var (navrh, subory) in casti.Values)
        {
            jednotky.Add(new JednotkaKniznice(navrh, subory.OrderBy(z => z.Cast).ThenBy(z => z.Subor).Select(z => z.Subor).ToList()));
        }

        foreach (var (navrh, subory) in struktury.Values)
        {
            if (navrh is null || navrh.Extra)
            {
                mimo.AddRange(subory);
            }
            else
            {
                // a Blu-ray or DVD is one film (a series on discs is confirmed by hand)
                jednotky.Add(new JednotkaKniznice(navrh.Druh == DruhTitulu.Film ? navrh : navrh with { Druh = DruhTitulu.Film, Seria = null, Epizody = [] },
                    subory.Order().ToList()));
            }
        }

        return (jednotky, mimo);
    }
}
