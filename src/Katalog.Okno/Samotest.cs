using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Katalog.Data;
using Katalog.Jadro;
using Katalog.Jadro.Model;

namespace Katalog.Okno;

/// <summary>
/// Self-test of the installed program (Katalog.exe --samotest), run by GitHub Actions on Windows: it creates an invented
/// library, writes and searches in it, opens the window in the light and the dark theme and takes screenshots. Its log
/// (selftest.log) and pictures go to KATALOG_SELFTEST_DIR. Only invented names are used; the user's settings and library
/// are never touched.
/// </summary>
internal static class Samotest
{
    private static string _vystup = "";
    private static int _chyby;

    /// <summary>The self-test runs: the sources of data stay off (nothing goes to the internet).</summary>
    public static bool Bezi { get; private set; }

    public static async Task<int> SpustiAsync()
    {
        Bezi = true;
        _vystup = Environment.GetEnvironmentVariable("KATALOG_SELFTEST_DIR") is { Length: > 0 } priecinok
            ? priecinok
            : Path.Combine(Path.GetTempPath(), Produkt.Nazov + "-samotest");
        Directory.CreateDirectory(_vystup);
        File.WriteAllText(Log, "");
        Zapis($"Samotest {Produkt.Nazov} {Produkt.VerziaText}");

        try
        {
            var cesta = Path.Combine(_vystup, "Samotest" + Produkt.PriponaKniznice);
            foreach (var subor in new[] { cesta, cesta + "-wal", cesta + "-shm" })
            {
                File.Delete(subor);
            }

            await KniznicaAsync(cesta);
            await OknoAsync(cesta);
        }
        catch (Exception ex)
        {
            Chyba("neočakávaná výnimka: " + ex);
        }

        Zapis(_chyby == 0 ? "Samotest OK" : $"Samotest: {_chyby} chýb");
        return _chyby == 0 ? 0 : 1;
    }

    private static string Log => Path.Combine(_vystup, "selftest.log");

    /// <summary>Id of the invented disk the self-test scans.</summary>
    private static long _diskId;

    private static Media.NastrojeFfmpeg? _ffmpeg;

    private static async Task KniznicaAsync(string cesta)
    {
        await using var kniznica = await Kniznica.OtvorAsync(cesta, new ZalohyKniznice(Path.Combine(_vystup, "Zalohy")));
        Over(kniznica.VerziaSchemy == Migracie.Posledna, $"knižnica má schému v{Migracie.Posledna}");
        Over(kniznica.Kontrola.JeVPoriadku, "kontrola integrity");

        // an invented disk: a folder of the runner's volume, scanned as a disk of a catalogue
        var disk = Path.Combine(_vystup, "VymyslenyDisk");
        if (Directory.Exists(disk))
        {
            Directory.Delete(disk, recursive: true);
        }

        Subor(disk, @"MOVIES\Vymyslený Šťastný film (2024)\Vymyslený Šťastný film (2024) - CZ EN FHD H264 SMALL.mkv", 1000);
        Subor(disk, @"TVSHOWS\Vymyslený seriál (2025)\Season 01\Vymyslený seriál S01E01 - CZ FHD H265 SMALL.mkv", 700);
        Subor(disk, @"TVSHOWS\Vymyslený seriál (2025)\Season 01\Vymyslený seriál S01E02 - CZ FHD H265 SMALL.mkv", 710);
        Subor(disk, @"Dokumenty\Vymyslená poznámka.txt", 12);

        // the film is a real (invented) video made by KATALOG's own FFmpeg: its technical data are read later
        _ffmpeg = Media.HladanieFfmpeg.Najdi(null, AppContext.BaseDirectory);
        Over(_ffmpeg is { Zdroj: Media.ZdrojFfmpeg.Pribaleny, Ffmpeg: not null }, $"FFmpeg nainštalovaný s KATALOGOM, overený manifestom ({_ffmpeg?.Zdroj})");
        if (_ffmpeg?.Ffmpeg is { } ffmpeg)
        {
            await VymysleneVideoAsync(ffmpeg, Path.Combine(disk, @"MOVIES\Vymyslený Šťastný film (2024)\Vymyslený Šťastný film (2024) - CZ EN FHD H264 SMALL.mkv"));
        }

        var zvazky = Skenovanie.Zvazky.Systemove;
        var identita = zvazky.Identita(disk);
        Over(identita is not null, $"identita zväzku ({identita?.Druh}, sériové číslo {(identita?.SerioveCisloZvazku is null ? "chýba" : "je")})");
        if (identita is null)
        {
            return;
        }

        var koren = Skenovanie.RozpoznanieDiskov.RelativnyKoren(Path.GetPathRoot(disk)!, disk);
        _diskId = await HlavneOkno.PridajDiskAsync(kniznica,
            new VolbaDisku(disk, identita, koren, "Vymyslený disk", null, "Vymyslená videotéka", zvazky.Miesto(disk).Volne, Nahlady: true));
        var ulozisko = new Skenovanie.LokalneUlozisko(disk, identita.Druh, zvazky);
        var prvy = await Ulohy.SkenovanieDisku.SpustiAsync(kniznica, _diskId, ulozisko, null, CancellationToken.None);
        Over(prvy is { Stav: StavUlohy.Hotova, Zmeny.Pridane: 4, Priecinky: 7 }, $"prvý sken: {prvy.Stav}, {prvy.Zmeny}, {prvy.Priecinky} priečinkov");

        await kniznica.Zapis.ZapisAsync(s =>
        {
            using var prikaz = s.CreateCommand();
            prikaz.CommandText = """
                INSERT INTO titul(id, druh, nazov, rok, vytvoreny, zmeneny) VALUES (1, 1, 'Vymyslený Šťastný film', 2024, 0, 0);
                INSERT INTO verzia(id, titul_id, potvrdena, vytvorena) VALUES (1, 1, 1, 0);
                INSERT INTO verzia_subor(verzia_id, subor_id) SELECT 1, id FROM subor WHERE nazov LIKE 'Vymyslený Šťastný film%';
                """;
            prikaz.ExecuteNonQuery();
        });

        using (var citanie = await kniznica.OtvorCitanieAsync())
        {
            var najdene = Fulltext.Najdi(citanie, "stastny film");
            Over(najdene.Any(n => n.Druh == DruhHladania.Subor) && najdene.Contains((DruhHladania.Titul, 1L)), "hľadanie bez diakritiky nájde súbor aj titul");
        }

        var bezZmien = await Ulohy.SkenovanieDisku.SpustiAsync(kniznica, _diskId, ulozisko, null, CancellationToken.None);
        Over(bezZmien.Zmeny == default, $"sken bez zmien nič nezapísal ({bezZmien.Zmeny})");
        File.Delete(Path.Combine(disk, "TVSHOWS", "Vymyslený seriál (2025)", "Season 01", "Vymyslený seriál S01E02 - CZ FHD H265 SMALL.mkv"));
        var poZmazani = await Ulohy.SkenovanieDisku.SpustiAsync(kniznica, _diskId, ulozisko, null, CancellationToken.None);
        Over(poZmazani.Zmeny == new PocetZmien(0, 0, 1), $"zmazaný súbor sa označil ({poZmazani.Zmeny})");

        using var c = await kniznica.OtvorCitanieAsync();
        var zalohy = new ZalohyKniznice(Path.Combine(_vystup, "Zalohy"));
        var zaloha = zalohy.Vytvor(c, kniznica.VerziaSchemy);
        Over(File.Exists(zaloha) && Path.GetFileName(zaloha).StartsWith(Produkt.Nazov + "-zaloha-0001-", StringComparison.Ordinal), "číslovaná záloha");
    }

    private static async Task OknoAsync(string cesta)
    {
        var nastavenia = Nastavenia.Nacitaj(Path.Combine(_vystup, "nastavenia"));
        // the self-test never asks the internet; it starts reading technical data itself (to wait for it)
        nastavenia.AutomatickeAktualizacie = false;
        nastavenia.CitatTechnickeUdaje = false;
        var okno = new HlavneOkno(nastavenia, cesta);
        okno.Show();
        await okno.Pripravene;
        Over(okno.Kniznica is { Kontrola.JeVPoriadku: true }, "okno otvorilo knižnicu");
        Over(okno.Title == Produkt.Nazov, "názov okna");
        Over(okno.Pripojenie?.Pripojene.ContainsKey(_diskId) == true, "vymyslený disk je rozpoznaný ako pripojený podľa zväzku");

        App.NastavMotiv("Light");
        okno.VyberDisk(_diskId);
        await SnimkaAsync(okno, "hlavne-okno-svetle.png");
        App.NastavMotiv("Dark");
        await SnimkaAsync(okno, "hlavne-okno-tmave.png");

        var vysledok = await okno.SkenujAsync(_diskId);
        Over(vysledok is { Stav: StavUlohy.Hotova }, $"sken z okna: {vysledok?.Stav}");

        // technical data: the film is read (with a preview), the two empty episodes are damaged files
        Over(okno.Ffmpeg?.Zdroj == Media.ZdrojFfmpeg.Pribaleny, $"okno používa FFmpeg nainštalovaný s KATALOGOM ({okno.Ffmpeg?.Zdroj})");
        var citanie = await okno.CitajAsync(_diskId, pouzivatel: true);
        Over(citanie is { Stav: StavUlohy.Hotova, Precitane: 2, Necitatelne: 1 }, $"technické údaje z okna: {citanie}");
        if (okno.Kniznica is { } k)
        {
            using var c = k.OtvorCitanie();
            var nikto = new KontextHladania([], DateTime.Now);
            var film = Hladanie.Najdi(c, Jadro.Dotazy.Dotaz.Citaj("kvalita:sd zvuk:sk typ:video"), nikto).Polozky;
            Over(film.Count == 1 && TechnickeUdaje.Citaj(c, film[0].Id) is { Nahlady.Count: 1, Udaje.Sirka: 320 },
                "hľadanie podľa technických údajov a náhľad filmu");
            Over(Hladanie.Najdi(c, Jadro.Dotazy.Dotaz.Citaj("poskodene:ano"), nikto).Polozky.Count == 1, "poškodený súbor sa dá nájsť");
            Over(UlohyKniznice.Cakajuce(c).Count == 0, "fronta úloh je po skene a čítaní prázdna");
        }

        App.NastavMotiv("Light");
        if (okno.Kniznica is { } kniznica && okno.Pripojenie is { } pripojenie)
        {
            IReadOnlyList<KatalogKniznice> katalogy;
            IReadOnlyList<Disk> disky;
            using (var c = kniznica.OtvorCitanie())
            {
                katalogy = KatalogyADisky.Katalogy(c);
                disky = KatalogyADisky.Disky(c);
            }

            var pridat = new PridatDiskOkno(katalogy, disky, pripojenie.Zvazky, katalogy[0].Id) { Owner = okno };
            pridat.Show();
            await Task.Delay(1500);
            await SnimkaAsync(pridat, "pridat-disk-svetle.png");
            pridat.Close();
        }

        await HladanieAsync(okno);
        await KnizniceAsync(okno);
        await PrehladyAsync(okno);

        App.NastavMotiv("Dark");
        var oPrograme = new OProgrameOkno { Owner = okno };
        oPrograme.Show();
        await SnimkaAsync(oPrograme, "o-programe-tmave.png");
        oPrograme.Close();

        App.NastavMotiv("System");
        okno.Close();
        Over(okno.Kniznica is null, "okno pri zatvorení zavrelo knižnicu");
    }

    /// <summary>Searching, filters, saved searches and playing from the window (no player starts: the start is only checked).</summary>
    private static async Task HladanieAsync(HlavneOkno okno)
    {
        App.NastavMotiv("Light");
        okno.PoleHladania.Text = "stastny film";
        var vysledky = await okno.HladajAsync();
        Over(vysledky?.Polozky.Count == 2 && vysledky.Polozky[0].Druh == DruhHladania.Priecinok,
            $"hľadanie z okna bez diakritiky: priečinok a súbor ({vysledky?.Polozky.Count})");
        Over(okno.PanelVysledkov.Visibility == Visibility.Visible, "výsledky sú v strede okna");

        Jadro.Prehravanie.Spustenie? spustenie = null;
        okno.SpustacPrehravania = s => spustenie = s;
        okno.ZoznamVysledkov.SelectedItem = okno.ZoznamVysledkov.Items.OfType<PolozkaVysledku>().FirstOrDefault(p => !p.JePriecinok);
        await SnimkaAsync(okno, "hladanie-svetle.png");
        var hra = await okno.PrehrajVybraneAsync();
        Over(hra && spustenie is { CezShell: true } s1 && s1.Program.EndsWith("Vymyslený Šťastný film (2024) - CZ EN FHD H264 SMALL.mkv", StringComparison.Ordinal) && File.Exists(s1.Program),
            "prehratie súboru predvoleným prehrávačom (cesta na pripojenom disku)");

        okno.PoleHladania.Text = "season typ:priecinok";
        await okno.HladajAsync();
        okno.ZoznamVysledkov.SelectAll();
        spustenie = null;
        hra = await okno.PrehrajVybraneAsync();
        Over(hra && spustenie?.Program.EndsWith("Vymyslený seriál S01E01 - CZ FHD H265 SMALL.mkv", StringComparison.Ordinal) == true,
            "prehratie priečinka sezóny (zmazaná epizóda sa nehrá)");

        okno.PoleHladania.Text = "typ:video pripojene:ano velkost:>=500B -ukazka";
        Over((await okno.HladajAsync())?.Polozky.Count == 2, "filtre v dotaze: typ, pripojené, veľkosť, záporné slovo");
        okno.PoleHladania.Text = "Vymyslen stitok:vymysleny";
        await okno.HladajAsync();
        Over(okno.UpozorneniaHladania.Visibility == Visibility.Visible, "neznáme pole sa ohlási");
        okno.SpustacPrehravania = null;

        if (okno.Kniznica is { } kniznica)
        {
            await kniznica.Zapis.ZapisAsync(s => UlozeneHladania.Uloz(s, "Videá na pripojených diskoch", "typ:video pripojene:ano"));
            await kniznica.Zapis.ZapisAsync(s => UlozeneHladania.Uloz(s, "Veľké filmy", "typ:video velkost:>20GB"));
            IReadOnlyList<KatalogKniznice> katalogy;
            IReadOnlyList<Disk> disky;
            using (var c = kniznica.OtvorCitanie())
            {
                katalogy = KatalogyADisky.Katalogy(c);
                disky = KatalogyADisky.Disky(c);
            }

            var filtre = new FiltreOkno("šťastný film typ:video velkost:1GB..20GB zmenene:2024 disk:1 pripojene:ano", katalogy, disky) { Owner = okno };
            filtre.Show();
            await SnimkaAsync(filtre, "filtre-svetle.png");
            filtre.Close();
        }

        okno.PoleHladania.Text = "";
        Over(okno.PanelVysledkov.Visibility == Visibility.Collapsed, "prázdne pole hľadania zavrie výsledky");

        App.NastavMotiv("Dark");
        okno.NavigaciaUlozene.IsSelected = true;
        Over(okno.ZoznamUlozenych.Items.Count == 2, "uložené hľadania v okne");
        await SnimkaAsync(okno, "ulozene-hladania-tmave.png");

        var nastavenia = new NastaveniaOkno(Nastavenia.Nacitaj(Path.Combine(_vystup, "nastavenia")), [], okno.Ffmpeg,
            Tajomstva.Nacitaj(Path.Combine(_vystup, "nastavenia"))) { Owner = okno };
        nastavenia.Show();
        await SnimkaAsync(nastavenia, "nastavenia-tmave.png");
        nastavenia.Close();
    }

    /// <summary>
    /// The library of titles (stage E4) with a stand-in source: recognition from names, data and posters filled in, the
    /// poster wall and list, a title waiting for confirmation chosen from candidates, a collection with a missing part,
    /// playing a series, the sources tab of Settings.
    /// </summary>
    private static async Task KnizniceAsync(HlavneOkno okno)
    {
        if (okno.SpravcaTitulov is not { } spravca || okno.Kniznica is not { } kniznica)
        {
            Chyba("okno nemá knižnicu titulov");
            return;
        }

        // a film without a year: it waits for confirmation
        Subor(Path.Combine(_vystup, "VymyslenyDisk"), @"Filmy\Vymyslený strážca.avi", 900);
        var plagaty = new Dictionary<string, byte[]>(StringComparer.Ordinal)
        {
            ["/samotest-film.jpg"] = Plagat("Vymyslený Šťastný film", Color.FromRgb(0x2B, 0x58, 0x9C)),
            ["/samotest-serial.jpg"] = Plagat("Vymyslený seriál", Color.FromRgb(0x8A, 0x3B, 0x12)),
            ["/samotest-diel2.jpg"] = Plagat("Vymyslený Šťastný film 2", Color.FromRgb(0x1E, 0x6B, 0x3A)),
            ["/samotest-strazca.jpg"] = Plagat("Vymyslený strážca", Color.FromRgb(0x5B, 0x2C, 0x83)),
        };
        spravca.ZdrojePreSamotest = [new ZdrojSamotestu()];
        spravca.ObrazkyPreSamotest = (adresa, _) => Task.FromResult<Ulohy.ObrazokZdroja?>(plagaty.TryGetValue(adresa.AbsolutePath.Replace("/t/p/w342", "", StringComparison.Ordinal), out var jpeg)
            ? new Ulohy.ObrazokZdroja(jpeg, 342, 513)
            : null);
        await okno.SkenujAsync(_diskId);
        await okno.DoplnKnizniciuAsync();

        App.NastavMotiv("Light");
        okno.NavigaciaFilmy.IsSelected = true;
        await okno.ZobrazKnizniciuAsync();
        var filmy = okno.PolozkySteny;
        Over(filmy.Any(f => f.Nazov == "Vymyslený Šťastný film" && f.MaPlagat && f.Titul is { NaPotvrdenie: false }), "film s plagátom zo zdroja, potvrdený sám (názov a rok sedia)");
        Over(filmy.Any(f => f.Nazov == "Vymyslený strážca" && f.Titul is { NaPotvrdenie: true }), "film bez roka čaká na potvrdenie");
        if (filmy.FirstOrDefault(f => f.Nazov == "Vymyslený Šťastný film") is { } film)
        {
            await okno.VyberPolozkuAsync(film);
            Over(okno.DetailZobrazeny is { Dej: not null, Kolekcie.Count: 1 } d && d.Verzie.Count == 1, "detail filmu: dej, verzia, kolekcia");
        }

        await SnimkaAsync(okno, "kniznica-plagaty-svetle.png");
        okno.ZobrazenieZoznam.IsChecked = true;
        await SnimkaAsync(okno, "kniznica-zoznam-svetle.png");
        okno.ZobrazeniePlagaty.IsChecked = true;

        App.NastavMotiv("Dark");
        okno.NavigaciaSerialy.IsSelected = true;
        await okno.ZobrazKnizniciuAsync();
        if (okno.PolozkySteny.FirstOrDefault(s => s.Nazov == "Vymyslený seriál") is { Titul: { } serial } dlazdica)
        {
            await okno.VyberPolozkuAsync(dlazdica);
            Over(okno.DetailZobrazeny?.Epizody is { Count: 3 } e && e.Count(x => x.Mam) == 1, "seriál: 3 epizódy zo zdroja, mám 1 (druhá zmazaná)");
            await SnimkaAsync(okno, "kniznica-serial-tmave.png");
            Jadro.Prehravanie.Spustenie? spustenie = null;
            okno.SpustacPrehravania = s => spustenie = s;
            var hra = await okno.PrehrajTitulAsync(serial.Id);
            Over(hra && spustenie?.Program.EndsWith("Vymyslený seriál S01E01 - CZ FHD H265 SMALL.mkv", StringComparison.Ordinal) == true, "prehratie seriálu z knižnice");
            okno.SpustacPrehravania = null;
        }
        else
        {
            Chyba("seriál v knižnici");
        }

        App.NastavMotiv("Light");
        okno.NavigaciaNaPotvrdenie.IsSelected = true;
        await okno.ZobrazKnizniciuAsync();
        if (okno.PolozkySteny.FirstOrDefault(p => p.Nazov == "Vymyslený strážca") is { Titul: { } strazca } naPotvrdenie)
        {
            await okno.VyberPolozkuAsync(naPotvrdenie);
            Over(okno.DetailZobrazeny?.Kandidati.Count == 2 && okno.DetailVyber.Visibility == Visibility.Visible, "na potvrdenie: dva návrhy vedľa seba");
            await SnimkaAsync(okno, "na-potvrdenie-svetle.png");
            var kandidat = okno.DetailZobrazeny!.Kandidati.First(k => k.Identifikator == "movie/9103");
            await okno.PotvrdKandidataAsync(strazca.Id, kandidat);
            await okno.ZobrazKnizniciuAsync();
            Over(okno.PolozkySteny.Count == 0, "po výbere nič nečaká na potvrdenie");
        }
        else
        {
            Chyba("titul na potvrdenie");
        }

        okno.NavigaciaKolekcie.IsSelected = true;
        await okno.ZobrazKnizniciuAsync();
        Over(okno.PolozkySteny.SingleOrDefault()?.Kolekcia is { Diely: 2, Mam: 1 }, "kolekcia: dva diely, mám jeden");
        await SnimkaAsync(okno, "kolekcie-svetle.png");

        using (var c = kniznica.OtvorCitanie())
        {
            var nikto = new KontextHladania([], DateTime.Now);
            Over(Hladanie.Najdi(c, Jadro.Dotazy.Dotaz.Citaj("kniznica:serial"), nikto).Polozky.Count == 1, "hľadanie súborov podľa knižnice (kniznica:serial)");
            Over(KniznicaTitulov.Najdi(c, Jadro.Dotazy.Dotaz.Citaj("mam:nie"), nikto, PohladKniznice.Vsetko).Any(t => t.Nazov == "Vymyslený Šťastný film 2"),
                "chýbajúci diel kolekcie (mam:nie)");
        }

        App.NastavMotiv("Dark");
        var nastavenia = new NastaveniaOkno(Nastavenia.Nacitaj(Path.Combine(_vystup, "nastavenia")), [], okno.Ffmpeg,
            Tajomstva.Nacitaj(Path.Combine(_vystup, "nastavenia")), zdroje: true) { Owner = okno };
        nastavenia.Show();
        await SnimkaAsync(nastavenia, "nastavenia-zdroje-tmave.png");
        nastavenia.Close();
    }

    /// <summary>
    /// Duplicates, titles on one disk, the overview of disks with where a file fits, the health of disks (a stand-in for the
    /// elevated helper writes invented numbers) and statistics – stage E5.
    /// </summary>
    private static async Task PrehladyAsync(HlavneOkno okno)
    {
        if (okno.Kniznica is not { } kniznica)
        {
            return;
        }

        // a second, bigger copy of the film elsewhere on the disk: a duplicate
        Subor(Path.Combine(_vystup, "VymyslenyDisk"), @"Zálohy\Vymyslený Šťastný film (2024).mkv", 1500);
        await okno.SkenujAsync(_diskId);
        await okno.DoplnKnizniciuAsync();

        App.NastavMotiv("Light");
        okno.NavigaciaDuplicity.IsSelected = true;
        await okno.ZobrazKnizniciuAsync();
        Over(okno.PolozkySteny.Count == 1 && okno.PolozkySteny[0].Nazov == "Vymyslený Šťastný film", "duplicity: film dvakrát");
        if (okno.PolozkySteny.FirstOrDefault() is { } film)
        {
            await okno.VyberPolozkuAsync(film);
            Over(okno.DetailZobrazeny is { Verzie.Count: 2, NajlepsieVerzie.Count: 1 }, "detail: dve verzie, jedna najlepšia");
            await SnimkaAsync(okno, "duplicity-svetle.png");
        }

        okno.NavigaciaNezalohovane.IsSelected = true;
        await okno.ZobrazKnizniciuAsync();
        Over(okno.PolozkySteny.Count >= 3, "s jedným diskom nie je zálohovaný nič");

        // the health of the disk: the helper's answer for its physical disk
        string seriove;
        using (var c = kniznica.OtvorCitanie())
        {
            seriove = KatalogyADisky.NajdiDisk(c, _diskId)?.Identita.SerioveCisloDisku ?? "";
        }

        if (seriove.Length == 0)
        {
            seriove = "SAMOTEST-DISK";
            await kniznica.Zapis.ZapisAsync(s =>
            {
                using var prikaz = s.CreateCommand();
                prikaz.CommandText = "UPDATE disk SET seriove_cislo_disku = $s WHERE id = $id";
                prikaz.Parameters.AddWithValue("$s", seriove);
                prikaz.Parameters.AddWithValue("$id", _diskId);
                prikaz.ExecuteNonQuery();
            });
        }

        okno.SpustacZdravia = async vystup =>
        {
            var odpoved = new Jadro.Zdravie.VystupZdravia(1,
            [
                new(0, "Vymyslený SSD", seriove, "NVMe", 38, 1_234, null, null, 0, 4, Jadro.Zdravie.Smart.Dobry, null, null),
                new(1, "Vymyslený USB disk", null, "USB", null, null, null, null, null, null, Jadro.Zdravie.Smart.Neznamy, null, "Rámček USB disku SMART neposiela ďalej."),
            ]);
            await File.WriteAllBytesAsync(vystup, System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(odpoved));
            return 0;
        };
        okno.NavigaciaPrehlad.IsSelected = true;
        await okno.ZobrazPrehladAsync();
        Over(await okno.ZistiZdravieAsync() == 1, "zdravie disku priradené podľa sériového čísla");
        Over(okno.Prehlad.SingleOrDefault() is { Tituly: >= 3, Zdravie: { Stav: StavZdravia.Dobry, Teplota: 38 } } r && r.LenTu == r.Tituly,
            "prehľad: tituly, len tu, zdravie");
        okno.PotrebneMiesto.Text = "1 MB";
        await okno.ZobrazPrehladAsync(new HashSet<long> { _diskId });
        await SnimkaAsync(okno, "prehlad-diskov-svetle.png");
        okno.SpustacZdravia = null;

        App.NastavMotiv("Dark");
        okno.NavigaciaStatistiky.IsSelected = true;
        await okno.ZobrazStatistikyAsync();
        Over(okno.SuhrnStatistik.Text.Contains("2 filmy", StringComparison.Ordinal), "štatistiky: " + okno.SuhrnStatistik.Text.Split('\n')[0]);
        await SnimkaAsync(okno, "statistiky-tmave.png");
    }

    /// <summary>An invented poster (a colour and the title), drawn here: no picture comes from the internet.</summary>
    private static byte[] Plagat(string nazov, Color farba)
    {
        var kresba = new DrawingVisual();
        using (var k = kresba.RenderOpen())
        {
            k.DrawRectangle(new LinearGradientBrush(farba, Color.FromRgb(0x10, 0x10, 0x10), 90), null, new Rect(0, 0, 342, 513));
            var text = new FormattedText(nazov, CultureInfo.GetCultureInfo("sk-SK"), FlowDirection.LeftToRight, new Typeface("Segoe UI Semibold"), 34,
                Brushes.White, 1) { MaxTextWidth = 300, TextAlignment = TextAlignment.Center };
            k.DrawText(text, new Point(21, 360));
        }

        var obrazok = new RenderTargetBitmap(342, 513, 96, 96, PixelFormats.Pbgra32);
        obrazok.Render(kresba);
        var koder = new JpegBitmapEncoder { QualityLevel = 85 };
        koder.Frames.Add(BitmapFrame.Create(obrazok));
        using var prud = new MemoryStream();
        koder.Save(prud);
        return prud.ToArray();
    }

    /// <summary>A stand-in for TMDB with invented titles; nothing goes to the internet.</summary>
    private sealed class ZdrojSamotestu : Jadro.Rozhrania.IZdrojUdajov
    {
        private static Uri Plagat(string meno) => new("https://image.tmdb.org/t/p/w342/" + meno);

        public ZdrojUdajov Zdroj => ZdrojUdajov.Tmdb;

        public bool JeZapnuty => true;

        public Task<IReadOnlyList<Jadro.Rozhrania.Kandidat>> HladajAsync(Jadro.Rozhrania.HladanieTitulu hladanie, CancellationToken zrusenie)
        {
            IReadOnlyList<Jadro.Rozhrania.Kandidat> vysledok = Normalizacia.Nazov(hladanie.Nazov) switch
            {
                "vymysleny stastny film" => [new(ZdrojUdajov.Tmdb, "movie/9101", DruhTitulu.Film, "Vymyslený Šťastný film", "Vymyslený Šťastný film", 2024, 0)],
                "vymysleny serial" => [new(ZdrojUdajov.Tmdb, "tv/9102", DruhTitulu.Serial, "Vymyslený seriál", "Vymyslený seriál", 2025, 0)],
                "vymysleny strazca" =>
                [
                    new(ZdrojUdajov.Tmdb, "movie/9103", DruhTitulu.Film, "Vymyslený strážca", "The Invented Guardian", 2008, 0),
                    new(ZdrojUdajov.Tmdb, "movie/9104", DruhTitulu.Film, "Vymyslený strážca 2", "The Invented Guardian 2", 2012, 0),
                ],
                _ => [],
            };
            return Task.FromResult(vysledok);
        }

        public Task<Jadro.Rozhrania.UdajeTitulu?> NacitajAsync(string identifikator, CancellationToken zrusenie) =>
            Task.FromResult<Jadro.Rozhrania.UdajeTitulu?>(identifikator switch
            {
                "movie/9101" => new(ZdrojUdajov.Tmdb, identifikator, DruhTitulu.Film, "Vymyslený Šťastný film", "Vymyslený Šťastný film", 2024,
                    "Vymyslený dej o šťastí, ktoré sa nedá nájsť hľadaním.", 104, 7.8, ["Komédia", "Dráma"],
                    [new("person/1", "Vymyslená Herečka", RolaOsoby.Herec, "Šťastena", 0), new("person/2", "Vymyslený Režisér", RolaOsoby.Rezia, null, 1)],
                    [], Plagat("samotest-film.jpg"), []),
                "tv/9102" => new(ZdrojUdajov.Tmdb, identifikator, DruhTitulu.Serial, "Vymyslený seriál", "Vymyslený seriál", 2025, "Vymyslený seriál o diskoch.", 45, 8.2,
                    ["Krimi"], [], [new(1, 1, "Prvý disk", new DateOnly(2025, 1, 6), null), new(1, 2, "Druhý disk", null, null), new(1, 3, "Tretí disk", null, null)],
                    Plagat("samotest-serial.jpg"), []),
                "movie/9103" => new(ZdrojUdajov.Tmdb, identifikator, DruhTitulu.Film, "Vymyslený strážca", "The Invented Guardian", 2008, "Vymyslený strážca stráži disky.",
                    120, 7.1, ["Akčný"], [], [], Plagat("samotest-strazca.jpg"), []),
                _ => null,
            });

        public Task<IReadOnlyList<Jadro.Rozhrania.UdajeKolekcie>> KolekcieAsync(string identifikator, CancellationToken zrusenie) =>
            Task.FromResult<IReadOnlyList<Jadro.Rozhrania.UdajeKolekcie>>(identifikator == "movie/9101"
                ?
                [
                    new(ZdrojUdajov.Tmdb, "collection/9100", "Vymyslené šťastné filmy", "Dva vymyslené filmy.",
                    [
                        new(ZdrojUdajov.Tmdb, "movie/9101", DruhTitulu.Film, "Vymyslený Šťastný film", null, 2024, 1),
                        new(ZdrojUdajov.Tmdb, "movie/9105", DruhTitulu.Film, "Vymyslený Šťastný film 2", null, 2026, 1, Plagat("samotest-diel2.jpg")),
                    ]),
                ]
                : []);
    }

    /// <summary>An invented two-second video (test picture, a tone, Slovak sound) made by FFmpeg itself.</summary>
    private static async Task VymysleneVideoAsync(string ffmpeg, string ciel)
    {
        var start = new System.Diagnostics.ProcessStartInfo(ffmpeg) { UseShellExecute = false, RedirectStandardError = true, CreateNoWindow = true };
        foreach (var a in new[]
                 {
                     "-v", "error", "-nostdin", "-f", "lavfi", "-i", "testsrc=size=320x180:rate=25:duration=2",
                     "-f", "lavfi", "-i", "sine=frequency=440:duration=2", "-c:v", "mjpeg", "-q:v", "8", "-c:a", "pcm_s16le",
                     "-metadata:s:a:0", "language=slo", "-y", ciel,
                 })
        {
            start.ArgumentList.Add(a);
        }

        using var proces = System.Diagnostics.Process.Start(start)!;
        var chyby = await proces.StandardError.ReadToEndAsync();
        await proces.WaitForExitAsync();
        Over(proces.ExitCode == 0 && new FileInfo(ciel).Length > 1000, "vymyslené video urobil FFmpeg KATALOGU" + (chyby.Length > 0 ? ": " + chyby : ""));
    }

    private static void Subor(string disk, string relativna, int velkost)
    {
        var cesta = Path.Combine(disk, relativna);
        Directory.CreateDirectory(Path.GetDirectoryName(cesta)!);
        File.WriteAllBytes(cesta, new byte[velkost]);
    }

    private static async Task SnimkaAsync(Window okno, string nazov)
    {
        await Dispatcher.CurrentDispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
        await Task.Delay(400);
        if (okno.Content is not FrameworkElement obsah || obsah.ActualWidth < 1)
        {
            Chyba($"snímka {nazov}: nič sa nevykreslilo");
            return;
        }

        var sirka = obsah.ActualWidth + obsah.Margin.Left + obsah.Margin.Right;
        var vyska = obsah.ActualHeight + obsah.Margin.Top + obsah.Margin.Bottom;
        var dpi = VisualTreeHelper.GetDpi(obsah);
        var obrazok = new RenderTargetBitmap(
            (int)(sirka * dpi.DpiScaleX), (int)(vyska * dpi.DpiScaleY), dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
        var pozadie = new DrawingVisual();
        using (var kreslenie = pozadie.RenderOpen())
        {
            kreslenie.DrawRectangle(Pozadie(okno), null, new Rect(0, 0, sirka, vyska));
        }

        obrazok.Render(pozadie);
        obrazok.Render(obsah);
        var koder = new PngBitmapEncoder();
        koder.Frames.Add(BitmapFrame.Create(obrazok));
        await using var prud = File.Create(Path.Combine(_vystup, nazov));
        koder.Save(prud);
        Zapis("snímka " + nazov);
    }

    /// <summary>
    /// Background of a screenshot: with the Fluent theme a window is transparent (Windows draws the Mica backdrop behind it),
    /// so the picture gets the theme's own application background instead.
    /// </summary>
    private static Brush Pozadie(Window okno)
    {
        if (okno.Background is SolidColorBrush { Color.A: 255 } plna)
        {
            return plna;
        }

        if (okno.TryFindResource("ApplicationBackgroundBrush") is SolidColorBrush { Color.A: 255 } tema)
        {
            return tema;
        }

        return Application.Current.ThemeMode == ThemeMode.Dark ? new SolidColorBrush(Color.FromRgb(0x20, 0x20, 0x20)) : Brushes.White;
    }

    private static void Over(bool podmienka, string co)
    {
        if (podmienka)
        {
            Zapis("OK " + co);
        }
        else
        {
            Chyba(co);
        }
    }

    private static void Chyba(string co)
    {
        _chyby++;
        Zapis(" ERROR " + co);
    }

    private static void Zapis(string riadok) =>
        File.AppendAllText(Log, DateTime.Now.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture) + " " + riadok + Environment.NewLine);
}
