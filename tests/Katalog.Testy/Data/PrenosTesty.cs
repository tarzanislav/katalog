using Katalog.Data;
using Katalog.Jadro.Dotazy;
using Katalog.Jadro.Model;
using Katalog.Ulohy;
using Microsoft.Data.Sqlite;

namespace Katalog.Testy;

/// <summary>Loans (N6), tags of titles and the export and import of a catalogue (A1) – stage E6.</summary>
public sealed class PrenosTesty
{
    private static readonly string[] SuboryPrveho =
    [
        @"MOVIES\Vymyslený film (2001)\Vymyslený film (2001) - EN FHD H264 SMALL.mkv",
        @"MOVIES\Vymyslený jediný (2002)\Vymyslený jediný (2002) - EN FHD H264 SMALL.mkv",
        @"TVSHOWS\Vymyslený seriál (2020)\Season 01\Vymyslený seriál S01E01 - CZ HD H264 SMALL.mkv",
        @"TVSHOWS\Vymyslený seriál (2020)\Season 01\Vymyslený seriál S01E02 - CZ HD H264 SMALL.mkv",
    ];

    private static readonly string[] SuboryDruheho =
    [
        @"Zálohy\Vymyslený film (2001)\Vymyslený film (2001) - EN FHD H264 SMALL.mkv",
        @"Zálohy\Vymyslený seriál S01E01.mkv",
    ];

    [Test]
    public async Task VypozickyAStitky()
    {
        using var priecinok = new DocasnyPriecinok();
        await using var kniznica = await Kniznica.OtvorAsync(priecinok.Subor("Kniznica.katalog"));
        var katalog = await kniznica.Zapis.ZapisAsync(s => KatalogyADisky.VytvorKatalog(s, "Vymyslená videotéka"));
        var prvy = await DuplicityTesty.DiskAsync(kniznica, priecinok.Cesta, katalog, "prvy", "1111-1111", SuboryPrveho);
        var druhy = await DuplicityTesty.DiskAsync(kniznica, priecinok.Cesta, katalog, "druhy", "2222-2222", SuboryDruheho);
        foreach (var disk in new[] { prvy, druhy })
        {
            await RozpoznanieDisku.SpustiAsync(kniznica, disk, "", CancellationToken.None);
        }

        var nikto = new KontextHladania([], DateTime.Now);
        using var c = kniznica.OtvorCitanie();
        IEnumerable<string> Tituly(string text) => KniznicaTitulov.Najdi(c, Dotaz.Citaj(text), nikto, PohladKniznice.Vsetko).Select(t => t.Nazov);
        int Subory(string text) => Hladanie.Najdi(c, Dotaz.Citaj(text), nikto).Polozky.Count;
        long Titul(string nazov) => KniznicaTitulov.Najdi(c, Dotaz.Citaj($"\"{nazov}\""), nikto, PohladKniznice.Vsetko).Single().Id;

        // a disk lent: its files and the titles on it
        var vypozicka = await kniznica.Zapis.ZapisAsync(s => Vypozicky.Pozicaj(s, druhy, null, "  Ján Vymyslený ", "na víkend", 100));
        Over.Postupnost(["Vymyslený film", "Vymyslený seriál"], Tituly("pozicane:ano"), "tituly na požičanom disku");
        Over.Postupnost(["Vymyslený film", "Vymyslený seriál"], Tituly("pozicane:jan"), "komu, bez diakritiky");
        Over.Postupnost([], Tituly("pozicane:peter"), "nikomu inému");
        Over.Postupnost(["Vymyslený jediný"], Tituly("pozicane:nie"), "nepožičané");
        Over.Rovne(2, Subory("pozicane:ano"), "súbory požičaného disku");
        Over.Pravda(!Dotaz.Citaj("pozicane:ano").HladaPriecinky, "požičané sú súbory, nie priečinky");
        await Over.VyhodiAsync<InvalidOperationException>(() => kniznica.Zapis.ZapisAsync(s => Vypozicky.Pozicaj(s, druhy, null, "Peter", null, 200)), "disk už je požičaný");

        // a title lent on its own
        var jediny = Titul("Vymyslený jediný");
        await kniznica.Zapis.ZapisAsync(s => Vypozicky.Pozicaj(s, null, jediny, "Peter Vymyslený", null, 150));
        Over.Postupnost(["Vymyslený jediný"], Tituly("pozicane:peter"), "titul požičaný Petrovi");
        Over.Rovne(1, Subory("pozicane:peter"), "jeho súbor");
        Over.Rovne(3, Subory("pozicane:ano"), "spolu tri súbory");
        Over.Rovne(3, Subory("-pozicane:ano typ:video"), "ostatné videá");

        var otvorene = Vypozicky.Otvorene(c);
        Over.Postupnost(["D002 druhy", "Vymyslený jediný (2002)"], otvorene.Select(v => v.Co), "čo je požičané");
        Over.Rovne("Ján Vymyslený", otvorene[0].Komu, "meno bez medzier navyše");
        Over.Rovne("na víkend", otvorene[0].Poznamka, "poznámka");
        await kniznica.Zapis.ZapisAsync(s => Vypozicky.Vrat(s, vypozicka, 300));
        Over.Rovne(1, Vypozicky.Otvorene(c).Count, "disk vrátený");
        Over.Rovne<long?>(300, Vypozicky.Historia(c, druhy, null).Single().Do, "história disku");
        Over.Postupnost(["Ján Vymyslený", "Peter Vymyslený"], Vypozicky.Mena(c).Order(StringComparer.Ordinal), "mená na výber");
        Over.Postupnost(["Vymyslený jediný"], Tituly("pozicane:ano"), "po vrátení disku");

        // tags
        var film = Titul("Vymyslený film");
        var serial = Titul("Vymyslený seriál");
        await kniznica.Zapis.ZapisAsync(s =>
        {
            StitkyTitulov.Nastav(s, [film], ["Vianoce", "Pre deti", " vianoce "]);
            StitkyTitulov.Pridaj(s, [serial], "VIANOCE");
        });
        Over.Postupnost(["Pre deti (1)", "Vianoce (2)"], StitkyTitulov.Zoznam(c).Select(st => $"{st.Nazov} ({st.Titulov})"), "štítky s počtami");
        Over.Postupnost(["Pre deti", "Vianoce"], StitkyTitulov.Titulu(c, film), "štítky filmu");
        Over.Postupnost(["Vymyslený film", "Vymyslený seriál"], Tituly("stitok:vianoce"), "stitok:vianoce");
        Over.Postupnost(["Vymyslený film"], Tituly("stitok:\"pre deti\""), "štítok s medzerou");
        Over.Postupnost(["Vymyslený jediný"], Tituly("-stitok:ano"), "bez štítku");
        Over.Rovne(5, Subory("stitok:vianoce"), "súbory titulov so štítkom (film dvakrát, tri epizódy)");

        var preDeti = StitkyTitulov.Zoznam(c).Single(st => st.Nazov == "Pre deti").Id;
        await kniznica.Zapis.ZapisAsync(s => StitkyTitulov.Premenuj(s, preDeti, "vianoce"));
        Over.Postupnost(["Vianoce (2)"], StitkyTitulov.Zoznam(c).Select(st => $"{st.Nazov} ({st.Titulov})"), "premenovanie na iný štítok ich spojí");
        await kniznica.Zapis.ZapisAsync(s => StitkyTitulov.Odober(s, [film, serial], StitkyTitulov.Zoznam(c).Single().Id));
        Over.Rovne(0, StitkyTitulov.Zoznam(c).Count, "nepoužitý štítok zmizne");
    }

    [Test]
    public async Task ExportAImportKatalogu()
    {
        using var priecinok = new DocasnyPriecinok();
        var subor = priecinok.Subor("Domov.katalog");
        var nikto = new KontextHladania([], DateTime.Now);

        // library A: two catalogues; "Domov" goes out with what the user said about its titles
        await using (var a = await Kniznica.OtvorAsync(priecinok.Subor(Path.Combine("A", "Kniznica.katalog"))))
        {
            var domov = await a.Zapis.ZapisAsync(s => KatalogyADisky.VytvorKatalog(s, "Domov"));
            var praca = await a.Zapis.ZapisAsync(s => KatalogyADisky.VytvorKatalog(s, "Práca"));
            var prvy = await DuplicityTesty.DiskAsync(a, priecinok.Subor("A"), domov, "prvy", "1111-1111", SuboryPrveho);
            var druhy = await DuplicityTesty.DiskAsync(a, priecinok.Subor("A"), domov, "druhy", "2222-2222", SuboryDruheho);
            var treti = await DuplicityTesty.DiskAsync(a, priecinok.Subor("A"), praca, "treti", "3333-3333",
                [@"Projekty\Vymyslený projekt (2015)\Vymyslený projekt (2015).mkv"]);
            foreach (var disk in new[] { prvy, druhy, treti })
            {
                await RozpoznanieDisku.SpustiAsync(a, disk, "", CancellationToken.None);
            }

            using var c = a.OtvorCitanie();
            var film = KniznicaTitulov.Najdi(c, Dotaz.Citaj("\"Vymyslený film\""), nikto, PohladKniznice.Filmy).Single().Id;
            var serial = KniznicaTitulov.Najdi(c, Dotaz.Citaj("\"Vymyslený seriál\""), nikto, PohladKniznice.Serialy).Single().Id;
            await a.Zapis.ZapisAsync(s =>
            {
                Tituly.NastavVidene(s, film, null, true, 500);
                StitkyTitulov.Nastav(s, [film, serial], ["Vianoce"]);
                Vypozicky.Pozicaj(s, druhy, null, "Ján Vymyslený", null, 600);
                Tituly.NastavVlastne(s, film, 9, "vymyslená poznámka", false, 700);
            });

            await PrenosKatalogu.ExportujAsync(a, domov, subor);
        }

        var vSubore = PrenosKatalogu.Katalogy(subor);
        Over.Rovne(1, vSubore.Count, "v súbore je len jeden katalóg");
        Over.Rovne(("Domov", 2, 6L), (vSubore[0].Nazov, vSubore[0].Diskov, vSubore[0].Suborov), "katalóg, disky a súbory");
        using (var s = new SqliteConnection($"Data Source={subor};Pooling=False"))
        {
            s.Open();
            using var p = s.CreateCommand();
            p.CommandText = "SELECT (SELECT count(*) FROM titul) || ' ' || (SELECT count(*) FROM disk) || ' ' || (SELECT count(*) FROM hladanie WHERE hladanie MATCH 'projekt')";
            Over.Rovne("3 2 0", p.ExecuteScalar(), "len tituly katalógu, nič z katalógu Práca");
        }

        // library B: its own catalogue "Domov" with disk number 1 and the same film from its own disk
        await using var b = await Kniznica.OtvorAsync(priecinok.Subor(Path.Combine("B", "Kniznica.katalog")));
        var vlastny = await b.Zapis.ZapisAsync(s => KatalogyADisky.VytvorKatalog(s, "Domov"));
        var vlastnyDisk = await DuplicityTesty.DiskAsync(b, priecinok.Subor("B"), vlastny, "vlastny", "9999-9999",
            [@"Filmy\Vymyslený film (2001)\Vymyslený film (2001).mkv"]);
        await RozpoznanieDisku.SpustiAsync(b, vlastnyDisk, "", CancellationToken.None);

        var vysledok = await PrenosKatalogu.ImportujAsync(b, subor);
        Over.Postupnost(["Domov (2)"], vysledok.Katalogy, "katalóg s voľným menom");
        Over.Rovne((2, 6L), (vysledok.Diskov, vysledok.Suborov), "disky a súbory");
        Over.Rovne((2, 1), (vysledok.NovychTitulov, vysledok.ZlucenychTitulov), "film sa spojil so známym, jediný a seriál sú nové");
        Over.Postupnost(["D001 → D003 prvy"], vysledok.Precislovane, "obsadené číslo disku");
        Over.Rovne(0, vysledok.Preskocene.Count, "nič nepreskočené");

        using (var c = b.OtvorCitanie())
        {
            IEnumerable<string> Tituly2(string text) => KniznicaTitulov.Najdi(c, Dotaz.Citaj(text), nikto, PohladKniznice.Vsetko).Select(t => t.Nazov);
            Over.Postupnost(["Vymyslený film", "Vymyslený jediný", "Vymyslený seriál"], Tituly2(""), "tituly bez zdvojenia");
            Over.Postupnost(["Vymyslený film"], Tituly2("verzie:3"), "film má tri verzie na troch diskoch");
            Over.Postupnost(["Vymyslený film", "Vymyslený seriál"], Tituly2("stitok:vianoce"), "štítky prišli");
            Over.Postupnost(["Vymyslený film", "Vymyslený seriál"], Tituly2("pozicane:jan"), "výpožička disku prišla");
            Over.Rovne(2, Hladanie.Najdi(c, Dotaz.Citaj("S01E01"), nikto).Polozky.Count, "fulltext nových súborov");
            Over.Rovne(3, Hladanie.Najdi(c, Dotaz.Citaj("katalog:\"Domov (2)\" kniznica:serial"), nikto).Polozky.Count, "epizódy ako verzie");
            var serial = KniznicaTitulov.Najdi(c, Dotaz.Citaj("kniznica:serial"), nikto, PohladKniznice.Serialy).Single();
            Over.Rovne(3, KniznicaTitulov.Detail(c, serial.Id)!.Verzie.Count, "verzie epizód seriálu");
        }

        // again: the library has those disks now – nothing comes in, no empty catalogue
        var znova = await PrenosKatalogu.ImportujAsync(b, subor);
        Over.Rovne(0, znova.Katalogy.Count, "žiadny nový katalóg");
        Over.Postupnost(["D001 prvy", "D002 druhy"], znova.Preskocene, "disky, ktoré knižnica už má");

        await Over.VyhodiAsync<CudziSuborException>(async () =>
        {
            var cudzi = priecinok.Subor("cudzi.katalog");
            await File.WriteAllTextAsync(cudzi, "toto nie je knižnica");
            await PrenosKatalogu.ImportujAsync(b, cudzi);
        }, "cudzí súbor");
    }
}
