using System.Net.Http;
using Katalog.Data;
using Katalog.Jadro;
using Katalog.Jadro.Model;
using Katalog.Jadro.Rozhrania;
using Katalog.Ulohy;
using Katalog.Zdroje;

namespace Katalog.Okno;

/// <summary>What the library of titles is doing now, as the status line shows it.</summary>
internal sealed record StavKnizniceTitulov(string Text, double? Podiel);

/// <summary>
/// The library of titles in the background (stage E4): videos of the disks become films and episodes (from names only,
/// fast and offline), then the switched-on sources fill in names, plots, posters, episodes and collections; Jellyfin's
/// collections and watching once per start. One run at a time; asked again while it runs, it runs once more after.
/// </summary>
internal sealed class SpravcaKniznice(Kniznica kniznica, Nastavenia nastavenia, Tajomstva tajomstva, UlozisteObrazkov obrazky)
{
    private static readonly HttpClient Http = VytvorKlienta();

    private CancellationTokenSource? _zrusenie;
    private Task? _beh;
    private bool _znova;
    private bool _jellyfinHotovy;

    /// <summary>The self-test's stand-in sources (invented answers, nothing goes to the internet).</summary>
    internal IReadOnlyList<IZdrojUdajov>? ZdrojePreSamotest { get; set; }

    /// <summary>The self-test's stand-in for downloading pictures.</summary>
    internal Func<Uri, CancellationToken, Task<ObrazokZdroja?>>? ObrazkyPreSamotest { get; set; }

    /// <summary>Raised on the window's thread when the state changes; null = nothing runs.</summary>
    public event Action<StavKnizniceTitulov?>? Zmena;

    /// <summary>Raised on the window's thread when the library changed (titles added or filled).</summary>
    public event Action? Zmenene;

    public bool Bezi => _zrusenie is not null;

    /// <summary>The last problem of a source (a wrong key, no internet); null when the last run was fine.</summary>
    public string? PoslednaChyba { get; private set; }

    /// <summary>The sources switched on with their keys (built anew each run: the keys or settings may have changed).</summary>
    public IReadOnlyList<IZdrojUdajov> Zdroje()
    {
        if (ZdrojePreSamotest is { } nahradne)
        {
            return nahradne;
        }

        if (Samotest.Bezi)
        {
            // the self-test never asks the internet, even on a computer with keys
            return [];
        }

        var kluce = tajomstva.Kluce();
        var zdroje = new List<IZdrojUdajov>();
        if (nastavenia.PouzivatTmdb && kluce.Tmdb.Kluc is { } tmdb)
        {
            zdroje.Add(new TmdbZdroj(Http, tmdb));
        }

        if (nastavenia.PouzivatHistra && kluce.Histra.Kluc is { } histra && HistraZdroj.JePlatnyToken(histra))
        {
            zdroje.Add(new HistraZdroj(Http, histra));
        }

        return zdroje;
    }

    private JellyfinZdroj? Jellyfin()
    {
        if (!nastavenia.PouzivatJellyfin || ZdrojePreSamotest is not null || Samotest.Bezi)
        {
            return null;
        }

        var jellyfin = new JellyfinZdroj(Http, nastavenia.JellyfinAdresa, tajomstva.Citaj("jellyfin"), nastavenia.JellyfinPouzivatel);
        return jellyfin.JeZapnuty ? jellyfin : null;
    }

    private DoplnenieUdajov Doplnenie() =>
        new(kniznica, Zdroje(), ObrazkyPreSamotest ?? StiahniAsync, obrazky, PoradieZdrojov.Predvolene);

    private static async Task<ObrazokZdroja?> StiahniAsync(Uri adresa, CancellationToken zrusenie)
    {
        try
        {
            return await StahovanieObrazkov.StiahniAsync(Http, adresa, zrusenie).ConfigureAwait(false) is { } o ? new ObrazokZdroja(o.Data, o.Sirka, o.Vyska) : null;
        }
        catch (ChybaZdroja)
        {
            // a missing poster is no reason to stop
            return null;
        }
    }

    /// <summary>
    /// Puts new videos into the library and lets the sources fill it (the self-test awaits it). While a run goes on, a
    /// new request marks that one more run is needed and waits for it (all on the window's thread).
    /// </summary>
    public Task SpustiAsync()
    {
        if (_beh is { IsCompleted: false })
        {
            _znova = true;
            return _beh;
        }

        _beh = BehAsync();
        return _beh;
    }

    private async Task BehAsync()
    {
        using var zrusenie = new CancellationTokenSource();
        _zrusenie = zrusenie;
        try
        {
            do
            {
                _znova = false;
                await JedenBehAsync(zrusenie.Token).ConfigureAwait(true);
            }
            while (_znova && !zrusenie.IsCancellationRequested);
        }
        catch (Exception ex) when (ex is ObjectDisposedException or InvalidOperationException or Microsoft.Data.Sqlite.SqliteException or OperationCanceledException)
        {
            // the library closed or the user stopped it: what is written stays, the rest goes on next time
        }
        finally
        {
            _zrusenie = null;
            Zmena?.Invoke(null);
        }
    }

    private async Task JedenBehAsync(CancellationToken zrusenie)
    {
        Zmena?.Invoke(new StavKnizniceTitulov("Knižnica: rozpoznávam filmy a seriály z názvov…", null));
        IReadOnlyList<Disk> disky;
        using (var c = kniznica.OtvorCitanie())
        {
            disky = KatalogyADisky.Disky(c);
        }

        var nove = 0;
        foreach (var disk in disky)
        {
            var vysledok = await Task.Run(() => RozpoznanieDisku.SpustiAsync(kniznica, disk.Id, disk.Identita.Menovka ?? disk.Nazov, zrusenie), zrusenie)
                .ConfigureAwait(true);
            nove += vysledok.Verzie;
        }

        if (nove > 0)
        {
            Zmenene?.Invoke();
        }

        var zdroje = Zdroje();
        if (zdroje.Count > 0)
        {
            var priebeh = new Progress<PriebehDoplnenia>(p =>
                Zmena?.Invoke(new StavKnizniceTitulov($"Knižnica: dopĺňam údaje a plagáty ({Format.Pocet(p.Hotove + 1)}) – {p.Titul}", null)));
            var doplnenie = Doplnenie();
            var vysledok = await Task.Run(() => doplnenie.SpustiAsync(priebeh, zrusenie), zrusenie).ConfigureAwait(true);
            PoslednaChyba = vysledok.Chyba;
            if (vysledok.Nacitane + vysledok.NaPotvrdenie > 0)
            {
                Zmenene?.Invoke();
            }
        }

        if (!_jellyfinHotovy && Jellyfin() is { } jellyfin)
        {
            Zmena?.Invoke(new StavKnizniceTitulov("Knižnica: preberám kolekcie a pozreté z Jellyfin…", null));
            try
            {
                await Task.Run(() => JellyfinAsync(jellyfin, zrusenie), zrusenie).ConfigureAwait(true);
                _jellyfinHotovy = true;
                Zmenene?.Invoke();
            }
            catch (ChybaZdroja ex)
            {
                PoslednaChyba = ex.Message;
            }
        }
    }

    private async Task JellyfinAsync(JellyfinZdroj jellyfin, CancellationToken zrusenie)
    {
        var kolekcie = await jellyfin.VsetkyKolekcieAsync(zrusenie).ConfigureAwait(false);
        var pozretia = await jellyfin.PozreteAsync(zrusenie).ConfigureAwait(false);
        await kniznica.Zapis.ZapisAsync(s =>
        {
            var teraz = Cas.Teraz();
            foreach (var kolekcia in kolekcie)
            {
                Tituly.ZapisKolekciu(s, kolekcia, null, PoradieZdrojov.Predvolene, teraz);
            }

            Tituly.ZapisPozretia(s, pozretia.Select(p => (p.Druh, p.TmdbId, p.Seria, p.Cislo, p.KedyUtc is { } k ? Cas.Z(k) : teraz)));
            Tituly.OdstranOpustene(s);
        }).ConfigureAwait(false);
    }

    /// <summary>The user confirms a candidate for a title; returns the title's id (another one when it merged).</summary>
    public Task<long> PotvrdAsync(long titulId, Kandidat kandidat) =>
        Task.Run(() => Doplnenie().PotvrdKandidataAsync(titulId, kandidat, CancellationToken.None));

    /// <summary>The user searches the sources for a title by another name or year: the new candidates are kept with it.</summary>
    public async Task<IReadOnlyList<Kandidat>> HladajAsync(long titulId, DruhTitulu druh, string nazov, int? rok)
    {
        var hladanie = new HladanieTitulu(nazov, rok, druh);
        var najdeni = new List<Kandidat>();
        foreach (var zdroj in Zdroje())
        {
            najdeni.AddRange(await zdroj.HladajAsync(hladanie, CancellationToken.None).ConfigureAwait(true));
            if (najdeni.Count > 0)
            {
                break;
            }
        }

        var (zoradeni, _) = ZhodaKandidata.Vyber(hladanie, najdeni);
        await kniznica.Zapis.ZapisAsync(s =>
        {
            foreach (var skupina in zoradeni.GroupBy(k => k.Zdroj))
            {
                Tituly.UlozKandidatov(s, titulId, skupina.Key, skupina.ToList());
            }
        }).ConfigureAwait(true);
        return zoradeni;
    }

    /// <summary>Reads the user's HISTRA lists into the library (mám / nemám); returns how many lists.</summary>
    public async Task<int> NacitajZoznamyHistraAsync(IProgress<string>? priebeh)
    {
        if (Zdroje().OfType<HistraZdroj>().FirstOrDefault() is not { } histra)
        {
            throw new ChybaZdroja("HISTRA nie je zapnutá alebo nemá token – nastav ho v Nastaveniach → Zdroje údajov.");
        }

        var zoznamy = await histra.ZoznamyAsync(CancellationToken.None).ConfigureAwait(true);
        foreach (var zoznam in zoznamy)
        {
            priebeh?.Report($"Čítam zoznam HISTRA „{zoznam.Nazov}“…");
            var polozky = await histra.CitajZoznamAsync(zoznam.Id, null, CancellationToken.None).ConfigureAwait(true);
            var plagaty = new Dictionary<string, UlozenyObrazok>(StringComparer.Ordinal);
            HashSet<string> sPlagatom;
            using (var c = kniznica.OtvorCitanie())
            {
                sPlagatom = Tituly.IdentifikatorySPlagatom(c, ZdrojUdajov.Histra, polozky.Select(p => p.Titul.Identifikator));
            }

            foreach (var p in polozky.Where(p => p.Udaje.Plagat is not null && !sPlagatom.Contains(p.Titul.Identifikator)))
            {
                if (await StiahniAsync(p.Udaje.Plagat!, CancellationToken.None).ConfigureAwait(true) is { } o)
                {
                    plagaty[p.Titul.Identifikator] = obrazky.Uloz(o.Data, DruhObrazka.Plagat, o.Sirka, o.Vyska);
                }
            }

            await kniznica.Zapis.ZapisAsync(s =>
            {
                var teraz = Cas.Teraz();
                Tituly.ZapisZoznamHistra(s, zoznam.Id.ToString(System.Globalization.CultureInfo.InvariantCulture), zoznam.Nazov,
                    polozky.Select(p => new PolozkaZoznamuHistra(p.Titul, p.TmdbId)).ToList(), teraz);
                foreach (var p in polozky)
                {
                    var titulId = Tituly.TitulZoZdroja(s, p.Titul, teraz);
                    long? plagat = plagaty.TryGetValue(p.Titul.Identifikator, out var o) ? UlozisteObrazkov.Zaregistruj(s, o, teraz) : null;
                    try
                    {
                        Tituly.ZapisUdaje(s, titulId, p.Udaje, plagat, PoradieZdrojov.Predvolene, teraz);
                    }
                    catch (InvalidOperationException)
                    {
                        // HISTRA says a film where the library knows a series (or the reverse): its data are left out
                    }
                }

                Tituly.OdstranOpustene(s);
            }).ConfigureAwait(true);
        }

        Zmenene?.Invoke();
        return zoznamy.Count;
    }

    public void Zrus() => _zrusenie?.Cancel();

    private static HttpClient VytvorKlienta()
    {
        var http = new HttpClient(new SocketsHttpHandler { AllowAutoRedirect = false, PooledConnectionLifetime = TimeSpan.FromMinutes(5) })
        {
            Timeout = TimeSpan.FromSeconds(30),
        };
        http.DefaultRequestHeaders.UserAgent.ParseAdd($"{Produkt.Nazov}/{Produkt.VerziaText}");
        return http;
    }
}
