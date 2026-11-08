using Katalog.Data;
using Katalog.Jadro.Model;
using Katalog.Jadro.Rozhrania;

namespace Katalog.Ulohy;

/// <summary>A picture a source offers, downloaded: its bytes (JPEG or PNG) and size.</summary>
public sealed record ObrazokZdroja(byte[] Data, int Sirka, int Vyska);

/// <summary>How a run of filling in ended.</summary>
/// <param name="Nacitane">Titles whose data were written.</param>
/// <param name="Potvrdene">Titles a source found exactly (name and year) and that left "Na potvrdenie".</param>
/// <param name="NaPotvrdenie">Titles left with candidates for the user.</param>
/// <param name="Chyba">Why the run stopped early (a wrong key, no internet), in Slovak; null when it did not.</param>
public sealed record VysledokDoplnenia(int Nacitane, int Potvrdene, int NaPotvrdenie, string? Chyba);

/// <summary>Progress: titles looked at in this run, and the title being looked at.</summary>
public readonly record struct PriebehDoplnenia(int Hotove, string Titul);

/// <summary>
/// Fills the library of titles from the switched-on sources (stage E4): a title with a known TMDB id gets its data,
/// poster, episodes and collection; one without is searched by name and year – taken only when exactly one candidate
/// has the same name and year (R5), else its candidates wait for the user. Only names, years and ids go to the sources.
/// Pictures are kept next to the library, so it works offline.
/// </summary>
public sealed class DoplnenieUdajov(
    Kniznica kniznica,
    IReadOnlyList<IZdrojUdajov> zdroje,
    Func<Uri, CancellationToken, Task<ObrazokZdroja?>> stiahniObrazok,
    UlozisteObrazkov obrazky,
    IReadOnlyList<ZdrojUdajov> poradie)
{
    /// <summary>A title without a match is searched again after a week (sources learn new titles).</summary>
    public static readonly TimeSpan HladatZnovaPo = TimeSpan.FromDays(7);

    /// <summary>A series is loaded again after two weeks (new episodes).</summary>
    public static readonly TimeSpan ObnovitSerialyPo = TimeSpan.FromDays(14);

    private const int Davka = 25;

    private IZdrojUdajov? Tmdb => zdroje.FirstOrDefault(z => z.Zdroj == ZdrojUdajov.Tmdb && z.JeZapnuty);

    public async Task<VysledokDoplnenia> SpustiAsync(IProgress<PriebehDoplnenia>? priebeh, CancellationToken zrusenie)
    {
        int nacitane = 0, potvrdene = 0, naPotvrdenie = 0, hotove = 0;
        var hladajuce = zdroje.Where(z => z.JeZapnuty).ToList();
        if (hladajuce.Count == 0)
        {
            return new VysledokDoplnenia(0, 0, 0, null);
        }

        var videne = new HashSet<long>();
        try
        {
            while (!zrusenie.IsCancellationRequested)
            {
                IReadOnlyList<TitulNaDoplnenie> davka;
                using (var c = kniznica.OtvorCitanie())
                {
                    davka = Tituly.NaDoplnenie(c, Davka, Cas.Teraz(), HladatZnovaPo.Ticks, ObnovitSerialyPo.Ticks);
                }

                // a title that comes back in the same run (a source failed for it) is not tried again now
                var nove = davka.Where(t => videne.Add(t.Id)).ToList();
                if (nove.Count == 0)
                {
                    break;
                }

                foreach (var titul in nove)
                {
                    zrusenie.ThrowIfCancellationRequested();
                    priebeh?.Report(new PriebehDoplnenia(hotove, titul.Nazov));
                    switch (await DoplnAsync(titul, hladajuce, zrusenie).ConfigureAwait(false))
                    {
                        case Vysledok.Nacitany:
                            nacitane++;
                            break;
                        case Vysledok.Potvrdeny:
                            nacitane++;
                            potvrdene++;
                            break;
                        case Vysledok.NaPotvrdenie:
                            naPotvrdenie++;
                            break;
                    }

                    hotove++;
                }
            }
        }
        catch (OperationCanceledException) when (zrusenie.IsCancellationRequested)
        {
        }
        catch (Exception ex) when (ex is ChybaZdroja or HttpRequestException)
        {
            // a wrong key or no internet: the rest waits for the next run
            return new VysledokDoplnenia(nacitane, potvrdene, naPotvrdenie, ex.Message);
        }

        return new VysledokDoplnenia(nacitane, potvrdene, naPotvrdenie, null);
    }

    /// <summary>
    /// The user picked a candidate for a title: its data are loaded (from TMDB when the candidate is a TMDB title and TMDB
    /// is on) and the title is confirmed. Returns the title's id (another one when it was merged into a known title).
    /// </summary>
    public async Task<long> PotvrdKandidataAsync(long titulId, Kandidat kandidat, CancellationToken zrusenie)
    {
        var udaje = await UdajeKandidataAsync(kandidat, zrusenie).ConfigureAwait(false);
        var plagat = await PlagatAsync(udaje.Plagat ?? kandidat.Plagat, zrusenie).ConfigureAwait(false);
        var id = await kniznica.Zapis.ZapisAsync(s =>
        {
            var konecny = Tituly.ZapisUdaje(s, titulId, udaje, Registruj(s, plagat), poradie, Cas.Teraz());
            Tituly.Potvrd(s, konecny);
            return konecny;
        }).ConfigureAwait(false);
        await KolekcieAsync(udaje, zrusenie).ConfigureAwait(false);
        return id;
    }

    private enum Vysledok
    {
        Nic,
        Nacitany,
        Potvrdeny,
        NaPotvrdenie,
    }

    private async Task<Vysledok> DoplnAsync(TitulNaDoplnenie titul, IReadOnlyList<IZdrojUdajov> hladajuce, CancellationToken zrusenie)
    {
        // a known TMDB title: its data straight away
        if (titul.Tmdb is { } tmdbId && Tmdb is { } tmdb)
        {
            var udaje = await tmdb.NacitajAsync(tmdbId, zrusenie).ConfigureAwait(false);
            if (udaje is null)
            {
                await kniznica.Zapis.ZapisAsync(s => Tituly.OznacNacitany(s, titul.Id, Cas.Teraz())).ConfigureAwait(false);
                return Vysledok.Nic;
            }

            await ZapisAsync(titul.Id, udaje, potvrdit: false, zrusenie).ConfigureAwait(false);
            return Vysledok.Nacitany;
        }

        if (titul.Tmdb is not null)
        {
            // known, but TMDB is off: nothing to search for
            await kniznica.Zapis.ZapisAsync(s => Tituly.OznacHladany(s, titul.Id, Cas.Teraz())).ConfigureAwait(false);
            return Vysledok.Nic;
        }

        // search by name (the original first: it is how UNIVERSUM names files) and year
        var hladanie = new HladanieTitulu(titul.Original ?? titul.Nazov, titul.Rok, titul.Druh);
        var najdeni = new List<Kandidat>();
        foreach (var zdroj in hladajuce)
        {
            var kandidati = await zdroj.HladajAsync(hladanie, zrusenie).ConfigureAwait(false);
            if (kandidati.Count == 0 && titul.Original is not null && titul.Original != titul.Nazov)
            {
                kandidati = await zdroj.HladajAsync(hladanie with { Nazov = titul.Nazov }, zrusenie).ConfigureAwait(false);
            }

            najdeni.AddRange(kandidati);
            if (kandidati.Count > 0)
            {
                // TMDB found it: HISTRA (which finds TMDB titles too) is not asked
                break;
            }
        }

        var (zoradeni, isty) = ZhodaKandidata.Vyber(hladanie, najdeni);
        if (isty is not null)
        {
            var udaje = await UdajeKandidataAsync(isty, zrusenie).ConfigureAwait(false);
            await ZapisAsync(titul.Id, udaje, potvrdit: true, zrusenie, isty.Plagat).ConfigureAwait(false);
            return titul.NaPotvrdenie ? Vysledok.Potvrdeny : Vysledok.Nacitany;
        }

        await kniznica.Zapis.ZapisAsync(s =>
        {
            foreach (var skupina in zoradeni.GroupBy(k => k.Zdroj))
            {
                Tituly.UlozKandidatov(s, titul.Id, skupina.Key, skupina.ToList());
            }

            Tituly.OznacHladany(s, titul.Id, Cas.Teraz());
        }).ConfigureAwait(false);
        return titul.NaPotvrdenie ? Vysledok.NaPotvrdenie : Vysledok.Nic;
    }

    /// <summary>The data of a candidate: TMDB's when it is a TMDB title and TMDB is on, else what the candidate says.</summary>
    private async Task<UdajeTitulu> UdajeKandidataAsync(Kandidat kandidat, CancellationToken zrusenie)
    {
        if (zdroje.FirstOrDefault(z => z.Zdroj == kandidat.Zdroj && z.JeZapnuty) is { } zdroj &&
            await zdroj.NacitajAsync(kandidat.Identifikator, zrusenie).ConfigureAwait(false) is { } udaje)
        {
            return udaje;
        }

        return new UdajeTitulu(kandidat.Zdroj, kandidat.Identifikator, kandidat.Druh, kandidat.Nazov, kandidat.Original, kandidat.Rok, null, null, null,
            [], [], [], kandidat.Plagat, []);
    }

    private async Task ZapisAsync(long titulId, UdajeTitulu udaje, bool potvrdit, CancellationToken zrusenie, Uri? nahradnyPlagat = null)
    {
        var plagat = await PlagatAsync(udaje.Plagat ?? nahradnyPlagat, zrusenie).ConfigureAwait(false);
        await kniznica.Zapis.ZapisAsync(s =>
        {
            var konecny = Tituly.ZapisUdaje(s, titulId, udaje, Registruj(s, plagat), poradie, Cas.Teraz());
            Tituly.OznacHladany(s, konecny, Cas.Teraz());
            if (potvrdit)
            {
                Tituly.Potvrd(s, konecny);
            }
        }).ConfigureAwait(false);
        await KolekcieAsync(udaje, zrusenie).ConfigureAwait(false);
    }

    /// <summary>The collections of a title, with posters of the collection and of its parts the library does not have yet.</summary>
    private async Task KolekcieAsync(UdajeTitulu udaje, CancellationToken zrusenie)
    {
        if (udaje.Druh != DruhTitulu.Film || zdroje.FirstOrDefault(z => z.Zdroj == udaje.Zdroj && z.JeZapnuty) is not { } zdroj)
        {
            return;
        }

        foreach (var kolekcia in await zdroj.KolekcieAsync(udaje.Identifikator, zrusenie).ConfigureAwait(false))
        {
            var plagat = await PlagatAsync(kolekcia.Plagat, zrusenie).ConfigureAwait(false);
            var plagatyDielov = new List<(Kandidat Diel, UlozenyObrazok? Plagat)>();
            HashSet<string> sPlagatom;
            using (var c = kniznica.OtvorCitanie())
            {
                sPlagatom = Tituly.IdentifikatorySPlagatom(c, kolekcia.Zdroj, kolekcia.Diely.Select(d => d.Identifikator));
            }

            foreach (var diel in kolekcia.Diely.Where(d => d.Identifikator != udaje.Identifikator && !sPlagatom.Contains(d.Identifikator)))
            {
                plagatyDielov.Add((diel, await PlagatAsync(diel.Plagat, zrusenie).ConfigureAwait(false)));
            }

            await kniznica.Zapis.ZapisAsync(s =>
            {
                var teraz = Cas.Teraz();
                Tituly.ZapisKolekciu(s, kolekcia, Registruj(s, plagat), poradie, teraz);
                foreach (var (diel, obrazok) in plagatyDielov.Where(p => p.Plagat is not null))
                {
                    Tituly.NastavPlagat(s, Tituly.TitulZoZdroja(s, diel, teraz), diel.Zdroj, Registruj(s, obrazok)!.Value, poradie, teraz);
                }
            }).ConfigureAwait(false);
        }
    }

    private async Task<UlozenyObrazok?> PlagatAsync(Uri? adresa, CancellationToken zrusenie)
    {
        if (adresa is null)
        {
            return null;
        }

        var obrazok = await stiahniObrazok(adresa, zrusenie).ConfigureAwait(false);
        return obrazok is null ? null : obrazky.Uloz(obrazok.Data, DruhObrazka.Plagat, obrazok.Sirka, obrazok.Vyska);
    }

    private static long? Registruj(Microsoft.Data.Sqlite.SqliteConnection spojenie, UlozenyObrazok? obrazok) =>
        obrazok is null ? null : UlozisteObrazkov.Zaregistruj(spojenie, obrazok, Cas.Teraz());
}
