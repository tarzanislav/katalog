using Katalog.Data;
using Katalog.Jadro.Dotazy;
using Katalog.Jadro.Model;
using Katalog.Jadro.Rozhrania;
using Katalog.Skenovanie;
using Katalog.Ulohy;
using Microsoft.Data.Sqlite;

namespace Katalog.Testy;

/// <summary>The library of titles: recognition of a scanned disk, data from sources with locks, merging, collections, HISTRA lists.</summary>
public sealed class TitulyTesty
{
    private static readonly IdentitaZvazku Identita = new(DruhDisku.Usb, "4444-4444", "{vymysleny-4}", null, "Knižnica", "NTFS", 1_000_000, null);

    [Test]
    public async Task RozpoznanieDiskuDoKniznice()
    {
        using var priecinok = new DocasnyPriecinok();
        await using var kniznica = await Kniznica.OtvorAsync(priecinok.Subor("Kniznica.katalog"));
        var diskId = await PripravAsync(kniznica, priecinok.Cesta, new[]
        {
            @"MOVIES\Vymyslený Šťastný film (2024) [tmdbid-123456]\Vymyslený Šťastný film (2024) - CZ EN FHD H264 SMALL.mkv",
            @"MOVIES\Vymyslená komédia (1999)\Vymyslená komédia (1999) - SK titulky EN HD H264 BIG.mkv",
            @"TVSHOWS\Vymyslený seriál (2025) [tmdbid-77]\Season 01\Vymyslený seriál S01E01 - CZ FHD H265 SMALL.mkv",
            @"TVSHOWS\Vymyslený seriál (2025) [tmdbid-77]\Season 01\Vymyslený seriál S01E02E03 - CZ FHD H265 SMALL.mkv",
            @"TVSHOWS\Vymyslený seriál (2025) [tmdbid-77]\Extras\Ako sa nakrúcalo.mkv",
            @"Filmy\Vymyslený epos (1975)\Vymyslený epos (1975) CD1.avi",
            @"Filmy\Vymyslený epos (1975)\Vymyslený epos (1975) CD2.avi",
            @"Filmy\Vymyslený epos (1975)\epos-sample.avi",
            @"Stiahnuté\Vymysleny.Hviezdny.Strazca.2019.1080p.BluRay.x264-SKUPINA.mkv",
            @"Disky\Vymyslený Blu-ray (2012)\BDMV\STREAM\00001.m2ts",
            @"Disky\Vymyslený Blu-ray (2012)\BDMV\STREAM\00002.m2ts",
            @"Rôzne\Vymyslené video.mp4",
        });

        var vysledok = await RozpoznanieDisku.SpustiAsync(kniznica, diskId, "Vymyslený disk", CancellationToken.None);
        Over.Rovne(new VysledokRozpoznania(8, 2), vysledok, "8 verzií, extra a ukážka bokom");

        using (var c = kniznica.OtvorCitanie())
        {
            Over.Rovne(0, Tituly.PocetNaZaradenie(c, diskId), "nič nečaká");
            var tituly = Riadky(c, "SELECT t.druh, t.nazov, t.rok FROM titul t ORDER BY t.nazov").ToList();
            string[] ocakavane =
            [
                "1 Vymyslené video ", "1 Vymyslený Blu-ray 2012", "1 Vymyslený epos 1975", "1 Vymyslený Šťastný film 2024",
                "1 Vymysleny Hviezdny Strazca 2019", "1 Vymyslená komédia 1999", "2 Vymyslený seriál 2025",
            ];
            Over.Postupnost(
                ocakavane.Order(StringComparer.Ordinal),
                tituly.Order(StringComparer.Ordinal), "tituly");
            Over.Rovne("2", Skalar(c, """
                SELECT count(*) FROM verzia_subor vs JOIN verzia v ON v.id = vs.verzia_id JOIN titul t ON t.id = v.titul_id
                WHERE t.nazov = 'Vymyslený epos'
                """), "CD1 a CD2 sú jedna verzia");
            Over.Rovne("2", Skalar(c, """
                SELECT count(*) FROM verzia_subor vs JOIN verzia v ON v.id = vs.verzia_id JOIN titul t ON t.id = v.titul_id
                WHERE t.nazov = 'Vymyslený Blu-ray'
                """), "celý BDMV je jedna verzia");
            Over.Postupnost(["1:1", "1:2", "1:3"], Riadky(c, "SELECT seria || ':' || cislo FROM epizoda ORDER BY seria, cislo"), "epizódy");
            Over.Rovne("1", Skalar(c, "SELECT count(*) FROM verzia_dalsia_epizoda"), "S01E02E03 patrí aj tretej epizóde");
            Over.Rovne("tv/77", Skalar(c, "SELECT hodnota FROM identifikator WHERE zdroj = 3 AND druh = 1 AND titul_id = (SELECT id FROM titul WHERE druh = 2)"), "TMDB seriálu");

            // UNIVERSUM's library and a TMDB mark are confirmed; the others wait (R5)
            Over.Postupnost(["Vymyslená komédia", "Vymyslený seriál", "Vymyslený Šťastný film"],
                Riadky(c, """
                    SELECT DISTINCT t.nazov FROM titul t
                    WHERE NOT EXISTS (SELECT 1 FROM verzia v LEFT JOIN epizoda e ON e.id = v.epizoda_id
                                      WHERE (v.titul_id = t.id OR e.titul_id = t.id) AND v.potvrdena = 0)
                    ORDER BY t.nazov
                    """).Order(StringComparer.Ordinal), "potvrdené");
            Over.Rovne("Vymyslený Šťastný film", Skalar(c, "SELECT original FROM titul WHERE rok = 2024"), "istý názov je originál");
            Over.Rovne("1", Skalar(c, "SELECT count(*) FROM povod_pola WHERE druh = 1 AND pole = 'original' AND zdroj = 1 AND objekt_id = (SELECT id FROM titul WHERE rok = 2024)"),
                "pôvod originálu je názov súboru");
            Over.Rovne("0", Skalar(c, "SELECT count(*) FROM povod_pola WHERE druh = 1 AND objekt_id = (SELECT id FROM titul WHERE rok = 2019)"),
                "odhad z release mena nemá pôvod");

            var naDoplnenie = Tituly.NaDoplnenie(c, 100, 1_000, 500);
            Over.Rovne(7, naDoplnenie.Count, "všetky tituly čakajú na zdroje");
            Over.Pravda(naDoplnenie.Single(t => t.Rok == 2024) is { Tmdb: "movie/123456", NaPotvrdenie: false }, "film s TMDB");
        }

        // a second scan finds nothing new; another file of the same film (other name) joins the title by its key
        Over.Rovne(new VysledokRozpoznania(0, 0), await RozpoznanieDisku.SpustiAsync(kniznica, diskId, "", CancellationToken.None), "druhý raz nič");
    }

    [Test]
    public async Task UdajeZoZdrojovSoZamkami()
    {
        using var priecinok = new DocasnyPriecinok();
        await using var kniznica = await Kniznica.OtvorAsync(priecinok.Subor("Kniznica.katalog"));
        var diskId = await PripravAsync(kniznica, priecinok.Cesta, new[]
        {
            @"Filmy\Vymyslený strážca (2008).mkv",
            @"Iné\Vymysleny.Strazca.2008.720p.mkv",
            @"MOVIES\Vymyslený seriálový film (2010)\Vymyslený seriálový film (2010) - EN HD H264 SMALL.mkv",
        });
        await RozpoznanieDisku.SpustiAsync(kniznica, diskId, "", CancellationToken.None);
        var poradie = PoradieZdrojov.Predvolene;
        long strazca;
        using (var c = kniznica.OtvorCitanie())
        {
            Over.Rovne("1", Skalar(c, "SELECT count(*) FROM titul WHERE rok = 2008"), "dve mená, jeden titul (kľúč bez diakritiky)");
            Over.Rovne("2", Skalar(c, "SELECT count(*) FROM verzia v JOIN titul t ON t.id = v.titul_id WHERE t.rok = 2008"), "dve verzie");
            strazca = long.Parse(Skalar(c, "SELECT id FROM titul WHERE rok = 2008"), System.Globalization.CultureInfo.InvariantCulture);
        }

        // the user locks the year; TMDB fills the rest, HISTRA cannot overwrite TMDB
        await kniznica.Zapis.ZapisAsync(s => Tituly.UpravPole(s, strazca, "dej", "Môj vlastný opis.", 10));
        var tmdb = Udaje(ZdrojUdajov.Tmdb, "movie/9001", "Vymyslený strážca", "The Invented Guardian", 2008, "Opis z TMDB.",
            zanre: ["Dobrodružný", "Akčný"], osoby: [new UdajeOsoby("person/1", "Vymyslený Herec", RolaOsoby.Herec, "Strážca", 0)],
            dalsie: [(ZdrojUdajov.Imdb, "tt0000001")]);
        var id = await kniznica.Zapis.ZapisAsync(s => Tituly.ZapisUdaje(s, strazca, tmdb, null, poradie, 20));
        Over.Rovne(strazca, id, "ten istý titul");
        await kniznica.Zapis.ZapisAsync(s => Tituly.ZapisUdaje(s, strazca,
            Udaje(ZdrojUdajov.Histra, "movie/55", "Vymyslený strážca (HISTRA)", "Iný originál", 2009, "Opis z HISTRA."), null, poradie, 30));
        using (var c = kniznica.OtvorCitanie())
        {
            Over.Rovne("Vymyslený strážca|The Invented Guardian|2008|Môj vlastný opis.", Skalar(c, $"SELECT nazov || '|' || original || '|' || rok || '|' || dej FROM titul WHERE id = {strazca}"),
                "TMDB pred HISTRA, zamknutý opis ostal");
            Over.Postupnost(["Akčný", "Dobrodružný"], Riadky(c, $"SELECT z.nazov FROM titul_zaner tz JOIN zaner z ON z.id = tz.zaner_id WHERE tz.titul_id = {strazca} ORDER BY z.nazov"), "žánre");
            Over.Rovne("Vymyslený Herec|Strážca", Skalar(c, $"SELECT o.meno || '|' || p.postava FROM titul_osoba p JOIN osoba o ON o.id = p.osoba_id WHERE p.titul_id = {strazca}"), "herec");
            Over.Postupnost(["3 movie/9001", "4 movie/55", "6 tt0000001"], Riadky(c, $"SELECT zdroj || ' ' || hodnota FROM identifikator WHERE titul_id = {strazca} ORDER BY zdroj"), "identifikátory");
            Over.Pravda(Riadky(c, $"SELECT kluc FROM titul_kluc WHERE titul_id = {strazca}").Contains("1:the invented guardian:2008"), "kľúč originálu");
        }

        // a later file named by the original finds the title
        var navrh = new NavrhTitulu(DruhTitulu.Film, "The Invented Guardian", 2008, null, null, [], null, [], TriedaKvality.HD, null, IstotaNavrhu.Pravdepodobny);
        long novySubor;
        using (var c = kniznica.OtvorCitanie())
        {
            novySubor = long.Parse(Skalar(c, "SELECT s.id FROM subor s WHERE s.nazov LIKE 'Vymyslený seriálový%'"), System.Globalization.CultureInfo.InvariantCulture);
        }

        // the file of the other film is moved by the user to the guardian, then merged by an identifier
        var verzia = await kniznica.Zapis.ZapisAsync(s => (long)Prikaz(s, $"SELECT verzia_id FROM verzia_subor WHERE subor_id = {novySubor}").ExecuteScalar()!);
        var presunuty = await kniznica.Zapis.ZapisAsync(s => Tituly.PresunVerziu(s, verzia, DruhTitulu.Film, navrh.Nazov, navrh.Rok, 40));
        Over.Rovne(strazca, presunuty, "presun podľa kľúča originálu nájde strážcu");
        using (var c = kniznica.OtvorCitanie())
        {
            Over.Rovne("0", Skalar(c, "SELECT count(*) FROM titul WHERE rok = 2010"), "opustený titul zmizol");
            Over.Rovne("3", Skalar(c, $"SELECT count(*) FROM verzia WHERE titul_id = {strazca}"), "tri verzie");
        }

        // a title of the same film created from a collection merges when its TMDB id arrives
        var diel = await kniznica.Zapis.ZapisAsync(s => Tituly.TitulZoZdroja(s,
            new Kandidat(ZdrojUdajov.Histra, "movie/56", DruhTitulu.Film, "Strážca znova", null, 2011, 1), 50));
        await kniznica.Zapis.ZapisAsync(s => Tituly.NastavVlastne(s, diel, 9, "Výborný.", true, 51));
        await kniznica.Zapis.ZapisAsync(s => Tituly.UpravPole(s, diel, "nazov", "Môj názov", 52));
        var zlucene = await kniznica.Zapis.ZapisAsync(s => Tituly.ZapisUdaje(s, diel, tmdb, null, poradie, 60));
        Over.Rovne(strazca, zlucene, "zlúčené do titulu s rovnakým TMDB");
        using (var c = kniznica.OtvorCitanie())
        {
            Over.Rovne($"Môj názov|9|Výborný.|1", Skalar(c, $"SELECT nazov || '|' || moje_hodnotenie || '|' || moja_poznamka || '|' || zelany FROM titul WHERE id = {strazca}"),
                "údaje používateľa prežijú zlúčenie");
            Over.Rovne("0", Skalar(c, $"SELECT count(*) FROM titul WHERE id = {diel}"), "zlúčený titul zmizol");
            Over.Pravda(Riadky(c, $"SELECT hodnota FROM identifikator WHERE titul_id = {strazca}").Contains("movie/56"), "identifikátor sa presunul");
        }

        // unlock: the next source fills the field again
        await kniznica.Zapis.ZapisAsync(s => Tituly.Odomkni(s, DruhObjektu.Titul, strazca, "dej"));
        await kniznica.Zapis.ZapisAsync(s => Tituly.ZapisUdaje(s, strazca, tmdb, null, poradie, 70));
        using (var c = kniznica.OtvorCitanie())
        {
            Over.Rovne("Opis z TMDB.", Skalar(c, $"SELECT dej FROM titul WHERE id = {strazca}"), "odomknutý opis");
        }

        // a film's data never go to a series
        var serial = await kniznica.Zapis.ZapisAsync(s => Tituly.TitulZoZdroja(s, new Kandidat(ZdrojUdajov.Tmdb, "tv/5", DruhTitulu.Serial, "Vymyslený seriál", null, 2020, 1), 80));
        await Over.VyhodiAsync<InvalidOperationException>(() => kniznica.Zapis.ZapisAsync(s => Tituly.ZapisUdaje(s, serial, tmdb, null, poradie, 81)), "film do seriálu");
    }

    [Test]
    public async Task KolekcieAZoznamyHistra()
    {
        using var priecinok = new DocasnyPriecinok();
        await using var kniznica = await Kniznica.OtvorAsync(priecinok.Subor("Kniznica.katalog"));
        var diskId = await PripravAsync(kniznica, priecinok.Cesta, new[] { @"MOVIES\Vymyslená sága (2001) [tmdbid-101]\Vymyslená sága (2001) - EN FHD H264 SMALL.mkv" });
        await RozpoznanieDisku.SpustiAsync(kniznica, diskId, "", CancellationToken.None);
        var kolekcia = new UdajeKolekcie(ZdrojUdajov.Tmdb, "collection/9", "Vymyslená sága – kolekcia", "Tri diely.",
        [
            new Kandidat(ZdrojUdajov.Tmdb, "movie/101", DruhTitulu.Film, "Vymyslená sága", "Vymyslená sága", 2001, 1),
            new Kandidat(ZdrojUdajov.Tmdb, "movie/102", DruhTitulu.Film, "Vymyslená sága 2", "Vymyslená sága 2", 2003, 1),
            new Kandidat(ZdrojUdajov.Tmdb, "movie/103", DruhTitulu.Film, "Vymyslená sága 3", "Vymyslená sága 3", 2005, 1),
        ]);
        var k1 = await kniznica.Zapis.ZapisAsync(s => Tituly.ZapisKolekciu(s, kolekcia, null, PoradieZdrojov.Predvolene, 10));
        var k2 = await kniznica.Zapis.ZapisAsync(s => Tituly.ZapisKolekciu(s, kolekcia, null, PoradieZdrojov.Predvolene, 11));
        Over.Rovne(k1, k2, "kolekcia sa nezdvojí");
        using (var c = kniznica.OtvorCitanie())
        {
            Over.Rovne("3", Skalar(c, $"SELECT count(*) FROM kolekcia_titul WHERE kolekcia_id = {k1}"), "tri diely");
            Over.Rovne("3", Skalar(c, "SELECT count(*) FROM titul"), "chýbajúce diely sú tituly bez verzie");
            Over.Postupnost(["Vymyslená sága 2", "Vymyslená sága 3"], Riadky(c, $"""
                SELECT t.nazov FROM kolekcia_titul k JOIN titul t ON t.id = k.titul_id
                WHERE k.kolekcia_id = {k1} AND NOT EXISTS (SELECT 1 FROM verzia v WHERE v.titul_id = t.id) ORDER BY k.poradie
                """), "chýbajúce diely");
        }

        var polozky = new List<PolozkaZoznamu>
        {
            new(new Kandidat(ZdrojUdajov.Histra, "movie/700", DruhTitulu.Film, "Vymyslená sága", "Vymyslená sága", 2001, 1), 101),
            new(new Kandidat(ZdrojUdajov.Histra, "tv/701", DruhTitulu.Serial, "Vymyslený seriál zo zoznamu", null, 2022, 1), null),
        };
        var zoznam = await kniznica.Zapis.ZapisAsync(s => Tituly.ZapisZoznamHistra(s, "42", "Chcem vidieť", polozky, 20));
        using (var c = kniznica.OtvorCitanie())
        {
            Over.Postupnost(["Vymyslená sága áno", "Vymyslený seriál zo zoznamu nie"], Riadky(c, $"""
                SELECT p.nazov || ' ' || CASE WHEN EXISTS (SELECT 1 FROM verzia v WHERE v.titul_id = p.titul_id)
                                              OR EXISTS (SELECT 1 FROM epizoda e JOIN verzia v ON v.epizoda_id = e.id WHERE e.titul_id = p.titul_id)
                                         THEN 'áno' ELSE 'nie' END
                FROM histra_polozka p WHERE p.zoznam_id = {zoznam} ORDER BY p.nazov
                """), "mám / nemám");
        }

        // the list shrinks: the entry goes, its title too (nobody needs it)
        await kniznica.Zapis.ZapisAsync(s => Tituly.ZapisZoznamHistra(s, "42", "Chcem vidieť", polozky.Take(1).ToList(), 30));
        Over.Rovne(1, await kniznica.Zapis.ZapisAsync(Tituly.OdstranOpustene), "titul zo zoznamu bez verzie zmizne");

        // a file the user says is no title leaves the library
        long subor;
        using (var c = kniznica.OtvorCitanie())
        {
            subor = long.Parse(Skalar(c, "SELECT subor_id FROM verzia_subor"), System.Globalization.CultureInfo.InvariantCulture);
        }

        await kniznica.Zapis.ZapisAsync(s => Tituly.MimoKniznice(s, [subor], pouzivatel: true));
        using (var c = kniznica.OtvorCitanie())
        {
            Over.Rovne("0", Skalar(c, "SELECT count(*) FROM verzia"), "verzia zmizla");
            Over.Rovne("3", Skalar(c, "SELECT count(*) FROM titul"), "titul ostal (je v kolekcii a v zozname)");
            Over.Rovne(0, Tituly.PocetNaZaradenie(c, diskId), "súbor bokom nečaká");
        }

        await kniznica.Zapis.ZapisAsync(s => Tituly.VratDoKniznice(s, [subor]));
        Over.Rovne(new VysledokRozpoznania(1, 0), await RozpoznanieDisku.SpustiAsync(kniznica, diskId, "", CancellationToken.None), "vrátený súbor sa zaradí znova");
    }

    [Test]
    public async Task KandidatiNaPotvrdenie()
    {
        using var priecinok = new DocasnyPriecinok();
        await using var kniznica = await Kniznica.OtvorAsync(priecinok.Subor("Kniznica.katalog"));
        var diskId = await PripravAsync(kniznica, priecinok.Cesta, new[] { @"Filmy\Vymyslený strážca.avi" });
        await RozpoznanieDisku.SpustiAsync(kniznica, diskId, "", CancellationToken.None);
        long titul;
        using (var c = kniznica.OtvorCitanie())
        {
            var t = Tituly.NaDoplnenie(c, 10, 100, 50).Single();
            Over.Pravda(t is { Nazov: "Vymyslený strážca", Rok: null, NaPotvrdenie: true, Hladany: null }, "neistý titul čaká");
            titul = t.Id;
        }

        Kandidat[] kandidati =
        [
            new(ZdrojUdajov.Tmdb, "movie/1", DruhTitulu.Film, "Vymyslený strážca", "The Invented Guardian", 2008, 0.8, new Uri("https://image.tmdb.org/t/p/w342/vymysleny.jpg")),
            new(ZdrojUdajov.Tmdb, "movie/2", DruhTitulu.Film, "Vymyslený strážca 2", null, 2012, 0.5),
        ];
        await kniznica.Zapis.ZapisAsync(s =>
        {
            Tituly.UlozKandidatov(s, titul, ZdrojUdajov.Tmdb, kandidati);
            Tituly.OznacHladany(s, titul, 100);
        });
        using (var c = kniznica.OtvorCitanie())
        {
            Over.Postupnost(kandidati, Tituly.Kandidati(c, titul), "kandidáti sa vrátia");
            Over.Rovne(0, Tituly.NaDoplnenie(c, 10, 120, 50).Count, "nedávno hľadaný sa nehľadá znova");
            Over.Rovne(1, Tituly.NaDoplnenie(c, 10, 200, 50).Count, "po čase znova");
        }

        await kniznica.Zapis.ZapisAsync(s => Tituly.Potvrd(s, titul));
        using (var c = kniznica.OtvorCitanie())
        {
            Over.Rovne("0|0", Skalar(c, "SELECT (SELECT count(*) FROM verzia WHERE potvrdena = 0) || '|' || (SELECT count(*) FROM kandidat)"), "potvrdené");
        }
    }

    [Test]
    public async Task HladanieVKniznici()
    {
        using var priecinok = new DocasnyPriecinok();
        await using var kniznica = await Kniznica.OtvorAsync(priecinok.Subor("Kniznica.katalog"));
        var diskId = await PripravAsync(kniznica, priecinok.Cesta, new[]
        {
            @"MOVIES\Vymyslená komédia (1999) [tmdbid-1]\Vymyslená komédia (1999) - CZ HD H264 SMALL.mkv",
            @"MOVIES\Vymyslený horor (2021) [tmdbid-2]\Vymyslený horor (2021) - EN FHD H264 SMALL.mkv",
            @"TVSHOWS\Vymyslený seriál (2025) [tmdbid-3]\Season 01\Vymyslený seriál S01E01 - CZ FHD H265 SMALL.mkv",
            @"TVSHOWS\Vymyslený seriál (2025) [tmdbid-3]\Season 01\Vymyslený seriál S01E02 - CZ FHD H265 SMALL.mkv",
            @"Filmy\Neistý film.avi",
        });
        await RozpoznanieDisku.SpustiAsync(kniznica, diskId, "", CancellationToken.None);
        var poradie = PoradieZdrojov.Predvolene;
        long komedia = 0, horor = 0, serial = 0;
        using (var c = kniznica.OtvorCitanie())
        {
            foreach (var t in Tituly.NaDoplnenie(c, 10, 1, 1))
            {
                switch (t.Tmdb)
                {
                    case "movie/1": komedia = t.Id; break;
                    case "movie/2": horor = t.Id; break;
                    case "tv/3": serial = t.Id; break;
                }
            }
        }

        await kniznica.Zapis.ZapisAsync(s =>
        {
            Tituly.ZapisUdaje(s, komedia, Udaje(ZdrojUdajov.Tmdb, "movie/1", "Vymyslená komédia", "The Invented Comedy", 1999, null,
                zanre: ["Komédia"], osoby: [new UdajeOsoby("person/7", "Vymyslená Herečka", RolaOsoby.Herec, null, 0)]) with { Hodnotenie = 6.5 }, null, poradie, 10);
            Tituly.ZapisUdaje(s, horor, Udaje(ZdrojUdajov.Tmdb, "movie/2", "Vymyslený horor", null, 2021, null,
                zanre: ["Horor"], osoby: [new UdajeOsoby("person/8", "Vymyslený Režisér", RolaOsoby.Rezia, null, 0)]) with { Hodnotenie = 8.1 }, null, poradie, 10);
            Tituly.ZapisKolekciu(s, new UdajeKolekcie(ZdrojUdajov.Tmdb, "collection/1", "Vymyslená zbierka hororov", null,
                [new Kandidat(ZdrojUdajov.Tmdb, "movie/2", DruhTitulu.Film, "Vymyslený horor", null, 2021, 1),
                 new Kandidat(ZdrojUdajov.Tmdb, "movie/4", DruhTitulu.Film, "Vymyslený horor 2", null, 2024, 1)]), null, poradie, 10);
            Tituly.NastavVlastne(s, komedia, 9, null, false, 11);
            Tituly.NastavVidene(s, komedia, null, true, 12);
        });

        var nikto = new KontextHladania([], DateTime.Now);
        using (var c = kniznica.OtvorCitanie())
        {
            IEnumerable<string> Tituly_(string text, PohladKniznice pohlad = PohladKniznice.Vsetko) =>
                KniznicaTitulov.Najdi(c, Dotaz.Citaj(text), nikto, pohlad).Select(t => t.Nazov);

            Over.Postupnost(["Neistý film", "Vymyslená komédia", "Vymyslený horor"], Tituly_("", PohladKniznice.Filmy), "filmy, ktoré mám");
            Over.Postupnost(["Vymyslený seriál"], Tituly_("", PohladKniznice.Serialy), "seriály");
            Over.Postupnost(["Neistý film"], Tituly_("", PohladKniznice.NaPotvrdenie), "na potvrdenie");
            Over.Postupnost(["Vymyslený horor 2"], Tituly_("mam:nie"), "chýbajúci diel kolekcie");
            Over.Postupnost(["Vymyslená komédia"], Tituly_("zaner:komedia"), "žáner bez diakritiky");
            Over.Postupnost(["Vymyslená komédia"], Tituly_("komedia"), "slovo v názve bez diakritiky");
            Over.Postupnost(["Vymyslená komédia"], Tituly_("komedia", PohladKniznice.Filmy).Where(_ => KniznicaTitulov.Detail(c, komedia)!.Povod["original"].Zdroj == ZdrojUdajov.NazovSuboru),
                "originál z knižnice UNIVERSUM ostal (TMDB je až za názvom súboru)");
            Over.Postupnost(["Vymyslený horor"], Tituly_("hodnotenie:>=8"), "hodnotenie zdroja");
            Over.Postupnost(["Vymyslená komédia"], Tituly_("moje:9 videne:ano"), "moje hodnotenie a pozreté");
            Over.Postupnost(["Vymyslená komédia"], Tituly_("herec:herečka"), "herec");
            Over.Postupnost(["Vymyslený horor"], Tituly_("rezia:\"vymyslený režisér\""), "réžia");
            Over.Postupnost(["Vymyslený horor", "Vymyslený horor 2"], Tituly_("kolekcia:hororov"), "kolekcia podľa mena");
            Over.Postupnost(["Vymyslený horor"], Tituly_("rok:2015..2022 kniznica:film"), "rok");
            Over.Postupnost(["Vymyslená komédia", "Vymyslený seriál"], Tituly_("-zaner:horor mam:ano rok:>1990"), "bez hororov");
            Over.Postupnost(["Vymyslený seriál"], Tituly_("pripona:mkv disk:1 kniznica:serial"), "podmienky súborov");

            // the same conditions find the files of the versions
            Over.Rovne(2, Hladanie.Najdi(c, Dotaz.Citaj("kniznica:serial"), nikto).Polozky.Count, "súbory seriálu");
            Over.Rovne("Vymyslená komédia (1999) - CZ HD H264 SMALL.mkv", Hladanie.Najdi(c, Dotaz.Citaj("zaner:komedia"), nikto).Polozky.Single().Nazov, "súbor komédie");
            Over.Rovne(4, Hladanie.Najdi(c, Dotaz.Citaj("videne:nie"), nikto).Polozky.Count, "nepozreté súbory");
            Over.Rovne(1, Hladanie.Najdi(c, Dotaz.Citaj("kniznica:potvrdit"), nikto).Polozky.Count, "súbor na potvrdenie");
            Over.Rovne(0, Hladanie.Najdi(c, Dotaz.Citaj("zaner:komedia typ:priecinok"), nikto).Polozky.Count, "priečinok nemá žáner");

            var detail = KniznicaTitulov.Detail(c, serial)!;
            Over.Pravda(detail.Polozka is { Druh: DruhTitulu.Serial, Mam: true, Epizody: 2, Verzie: 2 }, "detail seriálu");
            Over.Postupnost(["1:1 áno", "1:2 áno"], detail.Epizody.Select(e => $"{e.Seria}:{e.Cislo} {(e.Mam ? "áno" : "nie")}"), "epizódy");
            Over.Pravda(detail.Verzie.All(v => v.Pritomna && v.Subory.Single().DiskCislo == "D001"), "verzie na disku D001");
            var detailHororu = KniznicaTitulov.Detail(c, horor)!;
            Over.Pravda(detailHororu.Kolekcie.Single() is { Diely: 2, Mam: 1 }, "kolekcia: 2 diely, mám 1");
            Over.Pravda(detailHororu.Povod["nazov"].Zdroj == ZdrojUdajov.Tmdb && detailHororu.Zanre.SequenceEqual(["Horor"]), "pôvod a žáner");
            Over.Pravda(KniznicaTitulov.ZoznamKolekcii(c).Single() is { Nazov: "Vymyslená zbierka hororov", Diely: 2, Mam: 1 }, "zoznam kolekcií");
            Over.Rovne((3, 1, 1, 0), KniznicaTitulov.Pocty(c), "počty pohľadov");
        }
    }

    private static async Task<long> PripravAsync(Kniznica kniznica, string priecinok, IEnumerable<string> subory)
    {
        var disk = Path.Combine(priecinok, "disk");
        foreach (var subor in subory)
        {
            var cesta = Path.Combine(disk, subor.Replace('\\', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(cesta)!);
            await File.WriteAllBytesAsync(cesta, new byte[10]);
        }

        var katalog = await kniznica.Zapis.ZapisAsync(s => KatalogyADisky.VytvorKatalog(s, "Vymyslená videotéka"));
        var koren = kniznica.NovyIdPriecinka();
        var diskId = await kniznica.Zapis.ZapisAsync(s => KatalogyADisky.PridajDisk(s, new NovyDisk(katalog, "Vymyslený disk", Identita, "", null), koren));
        var sken = await SkenovanieDisku.SpustiAsync(kniznica, diskId, new LokalneUlozisko(disk, DruhDisku.Usb, new Zvazok(Identita)), null, CancellationToken.None);
        Over.Rovne(StavUlohy.Hotova, sken.Stav, "sken");
        return diskId;
    }

    private sealed class Zvazok(IdentitaZvazku identita) : IZvazky
    {
        public IReadOnlyList<PripojenyZvazok> Pripojene() => [];

        public IdentitaZvazku? Identita(string cesta) => Directory.Exists(cesta) ? identita : null;

        public (long? Velkost, long? Volne) Miesto(string cesta) => (null, null);
    }

    private static UdajeTitulu Udaje(
        ZdrojUdajov zdroj, string id, string nazov, string? original, int? rok, string? dej,
        IReadOnlyList<string>? zanre = null, IReadOnlyList<UdajeOsoby>? osoby = null, IReadOnlyList<(ZdrojUdajov, string)>? dalsie = null) =>
        new(zdroj, id, DruhTitulu.Film, nazov, original, rok, dej, 120, 7.4, zanre ?? [], osoby ?? [], [], null, dalsie ?? []);

    private static SqliteCommand Prikaz(SqliteConnection c, string sql)
    {
        var p = c.CreateCommand();
        p.CommandText = sql;
        return p;
    }

    private static string Skalar(SqliteConnection c, string sql)
    {
        using var p = Prikaz(c, sql);
        return Convert.ToString(p.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture) ?? "";
    }

    private static IEnumerable<string> Riadky(SqliteConnection c, string sql)
    {
        using var p = Prikaz(c, sql);
        using var r = p.ExecuteReader();
        var vysledok = new List<string>();
        while (r.Read())
        {
            vysledok.Add(string.Join(" ", Enumerable.Range(0, r.FieldCount).Select(i => Convert.ToString(r.GetValue(i), System.Globalization.CultureInfo.InvariantCulture))));
        }

        return vysledok;
    }
}
