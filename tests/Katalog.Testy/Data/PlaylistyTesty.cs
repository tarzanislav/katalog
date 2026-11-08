using Katalog.Data;
using Katalog.Jadro.Dotazy;
using Katalog.Jadro.Model;
using Katalog.Ulohy;

namespace Katalog.Testy;

/// <summary>Playlists, drawing at random, what plays next and suggestions (stage E7).</summary>
public sealed class PlaylistyTesty
{
    [Test]
    public void LosovanieJeStale()
    {
        // the sequence of SplitMix64 as an independent script computed it
        Over.Postupnost([3, 2, 4, 5, 8, 7, 0, 9, 6, 1], Playlisty.Losuj(Enumerable.Range(0, 10).ToList(), 42, 10), "zamiešanie 0–9");
        Over.Postupnost([3, 2, 4], Playlisty.Losuj(Enumerable.Range(0, 10).ToList(), 42, 3), "prvé tri sú tie isté");
        Over.Postupnost([97, 19, 77, 83, 47], Playlisty.Losuj(Enumerable.Range(0, 100).ToList(), 123_456_789, 5), "päť zo sto");
        Over.Rovne(4, Playlisty.Losuj(Enumerable.Range(0, 4).ToList(), 7, 10).Count, "viac, než je, sa nevylosuje");
        Over.Rovne(4, Playlisty.Losuj(Enumerable.Range(0, 4).ToList(), 7, 4).Distinct().Count(), "nič dvakrát");
    }

    [Test]
    public async Task PlaylistyAPrehratie()
    {
        using var priecinok = new DocasnyPriecinok();
        await using var kniznica = await Kniznica.OtvorAsync(priecinok.Subor("Kniznica.katalog"));
        var katalog = await kniznica.Zapis.ZapisAsync(s => KatalogyADisky.VytvorKatalog(s, "Vymyslená videotéka"));
        var prvy = await DuplicityTesty.DiskAsync(kniznica, priecinok.Cesta, katalog, "prvy", "1111-1111",
        [
            @"MOVIES\Vymyslený film (2001)\Vymyslený film (2001) - EN FHD H264 SMALL.mkv",
            @"MOVIES\Vymyslený jediný (2002)\Vymyslený jediný (2002) - EN FHD H264 SMALL.mkv",
            @"MOVIES\Vymyslený starší (1999)\Vymyslený starší (1999) - EN FHD H264 SMALL.mkv",
            @"TVSHOWS\Vymyslený seriál (2020)\Season 01\Vymyslený seriál S01E01 - CZ HD H264 SMALL.mkv",
            @"TVSHOWS\Vymyslený seriál (2020)\Season 01\Vymyslený seriál S01E02 - CZ HD H264 SMALL.mkv",
        ]);
        var druhy = await DuplicityTesty.DiskAsync(kniznica, priecinok.Cesta, katalog, "druhy", "2222-2222",
            [@"Zálohy\Vymyslený film (2001)\Vymyslený film (2001) - EN FHD H264 SMALL.mkv"]);
        foreach (var disk in new[] { prvy, druhy })
        {
            await RozpoznanieDisku.SpustiAsync(kniznica, disk, "", CancellationToken.None);
        }

        var nikto = new KontextHladania([], DateTime.Now);
        using var c = kniznica.OtvorCitanie();
        long Titul(string nazov) => KniznicaTitulov.Najdi(c, Dotaz.Citaj($"\"{nazov}\""), nikto, PohladKniznice.Vsetko).Single().Id;
        IEnumerable<string> Nazvy(long id) => Playlisty.Tituly(c, Playlisty.Najdi(c, id)!, nikto).Select(t => t.Nazov);
        var film = Titul("Vymyslený film");
        var jediny = Titul("Vymyslený jediný");
        var starsi = Titul("Vymyslený starší");
        var serial = Titul("Vymyslený seriál");

        // hand-made: the user's order
        var rucny = await kniznica.Zapis.ZapisAsync(s => Playlisty.Vytvor(s, " Na víkend ", DruhPlaylistu.Rucny, "ignorovaný dotaz", 5, 100));
        Over.Rovne(2, await kniznica.Zapis.ZapisAsync(s => Playlisty.Pridaj(s, rucny, [jediny, film, jediny])), "pridané dva");
        Over.Rovne(0, await kniznica.Zapis.ZapisAsync(s => Playlisty.Pridaj(s, rucny, [film])), "titul tam už je");
        Over.Postupnost(["Vymyslený jediný", "Vymyslený film"], Nazvy(rucny), "v poradí pridania");
        await kniznica.Zapis.ZapisAsync(s => Playlisty.Posun(s, rucny, film, -1));
        Over.Postupnost(["Vymyslený film", "Vymyslený jediný"], Nazvy(rucny), "film o miesto vyššie");
        await kniznica.Zapis.ZapisAsync(s =>
        {
            Playlisty.Pridaj(s, rucny, [serial, starsi]);
            Playlisty.Odober(s, rucny, [jediny]);
            Playlisty.Zorad(s, rucny, [starsi, serial]);
        });
        Over.Postupnost(["Vymyslený starší", "Vymyslený seriál", "Vymyslený film"], Nazvy(rucny), "odobratý a zoradený");
        var ulozeny = Playlisty.Najdi(c, rucny)!;
        Over.Rovne(("Na víkend", 3, (string?)null, (int?)null), (ulozeny.Nazov, ulozeny.Poloziek, ulozeny.Dotaz, ulozeny.Pocet), "ručný bez dotazu");

        // smart: a query, chronologically
        var filmy = await kniznica.Zapis.ZapisAsync(s => Playlisty.Vytvor(s, "Filmy", DruhPlaylistu.Inteligentny, "kniznica:film", null, 100));
        Over.Postupnost(["Vymyslený starší", "Vymyslený film", "Vymyslený jediný"], Nazvy(filmy), "podľa roka");

        // random: two of everything, the same until drawn again
        var nahodny = await kniznica.Zapis.ZapisAsync(s => Playlisty.Vytvor(s, "Prekvapenie", DruhPlaylistu.Nahodny, "", 2, 100));
        var prve = Nazvy(nahodny).ToList();
        Over.Rovne(2, prve.Distinct().Count(), "dva rôzne tituly");
        Over.Postupnost(prve, Nazvy(nahodny), "ten istý žreb");
        var ine = false;
        for (var i = 0; i < 20 && !ine; i++)
        {
            await kniznica.Zapis.ZapisAsync(s => Playlisty.Vylosuj(s, nahodny));
            ine = !Nazvy(nahodny).SequenceEqual(prve);
        }

        Over.Pravda(ine, "nový žreb dá iné tituly");
        await kniznica.Zapis.ZapisAsync(s => Playlisty.Uprav(s, nahodny, "Prekvapenie 3", "kniznica:film", 3));
        Over.Postupnost(["Vymyslený film", "Vymyslený jediný", "Vymyslený starší"], Nazvy(nahodny).Order(StringComparer.Ordinal), "tri filmy");
        await Over.VyhodiAsync<ArgumentException>(() => kniznica.Zapis.ZapisAsync(s => Playlisty.Vytvor(s, " ", DruhPlaylistu.Rucny, null, null, 1)), "bez názvu");

        // what plays next of a series, series begun
        long Epizoda(int cislo)
        {
            using var p = c.CreateCommand();
            p.CommandText = $"SELECT id FROM epizoda WHERE titul_id = {serial} AND seria = 1 AND cislo = {cislo}";
            return (long)p.ExecuteScalar()!;
        }

        Over.Rovne<long?>(Epizoda(1), Playlisty.DalsiaEpizoda(c, serial), "nič nevidené – prvá epizóda");
        Over.Rovne(0, KniznicaTitulov.Najdi(c, Dotaz.Citaj("rozpozerane:ano"), nikto, PohladKniznice.Vsetko).Count, "nezačatý seriál");
        await kniznica.Zapis.ZapisAsync(s => Tituly.NastavVidene(s, null, Epizoda(1), true, 200));
        Over.Rovne<long?>(Epizoda(2), Playlisty.DalsiaEpizoda(c, serial), "ďalšia nevidená");
        Over.Postupnost(["Vymyslený seriál"], KniznicaTitulov.Najdi(c, Dotaz.Citaj("rozpozerane:ano"), nikto, PohladKniznice.Vsetko).Select(t => t.Nazov), "rozpozeraný");
        Over.Rovne(2, Hladanie.Najdi(c, Dotaz.Citaj("rozpozerane:ano"), nikto).Polozky.Count, "súbory rozpozeraného seriálu");
        await kniznica.Zapis.ZapisAsync(s => Tituly.NastavVidene(s, null, Epizoda(2), true, 300));
        Over.Rovne<long?>(Epizoda(1), Playlisty.DalsiaEpizoda(c, serial), "všetko videné – znova od začiatku");
        Over.Rovne(0, KniznicaTitulov.Najdi(c, Dotaz.Citaj("rozpozerane:ano"), nikto, PohladKniznice.Vsetko).Count, "dopozeraný seriál");

        // the version to play: on the connected disk
        var naDruhom = Playlisty.VerziaNaPrehratie(c, film, null, new HashSet<long> { druhy }, PorovnanieVerzii.Predvolene);
        Over.Rovne(druhy, KniznicaTitulov.CielVerzie(c, naDruhom!.Value).Single().DiskId, "verzia na pripojenom disku");
        var naPrvom = Playlisty.VerziaNaPrehratie(c, film, null, new HashSet<long> { prvy }, PorovnanieVerzii.Predvolene);
        Over.Rovne(prvy, KniznicaTitulov.CielVerzie(c, naPrvom!.Value).Single().DiskId, "verzia na druhom pripojenom disku");
        Over.Pravda(Playlisty.VerziaNaPrehratie(c, film, null, new HashSet<long>(), PorovnanieVerzii.Predvolene) is not null, "aj bez pripojeného disku");
        Over.Pravda(Playlisty.VerziaNaPrehratie(c, serial, Epizoda(2), new HashSet<long>(), PorovnanieVerzii.Predvolene) is not null, "verzia epizódy");

        // suggestions
        var navrhy = Playlisty.Navrhy(c, nikto);
        Over.Pravda(navrhy.Any(n => n is { Nazov: "Nevidené filmy", Titulov: 3 }), "návrh: nevidené filmy");
        Over.Pravda(navrhy.All(n => n.Titulov >= 2), "len návrhy s aspoň dvoma titulmi");

        // what the web catalogue is made of (stage E9)
        var web = PodkladyWebu.Tituly(c, nikto);
        Over.Rovne(4, web.Count, "štyri tituly pre web");
        var filmNaWebe = web.Single(t => t.Titul.Id == film);
        Over.Postupnost(["Zálohy/Vymyslený film (2001)/Vymyslený film (2001) - EN FHD H264 SMALL.mkv", "MOVIES/Vymyslený film (2001)/Vymyslený film (2001) - EN FHD H264 SMALL.mkv"],
            filmNaWebe.Verzie.Select(v => v.Cesta).Order(StringComparer.Ordinal).Reverse(), "cesty verzií filmu");
        Over.Pravda(web.Single(t => t.Titul.Id == serial) is { Epizod: 2, Verzie.Count: 2 } s2 && s2.Verzie.All(v => v.Seria == 1), "epizódy seriálu");

        await kniznica.Zapis.ZapisAsync(s => Playlisty.Zmaz(s, rucny));
        Over.Rovne(2, Playlisty.Zoznam(c).Count, "playlist zmazaný");
    }
}
