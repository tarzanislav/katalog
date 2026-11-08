using Katalog.Jadro.Model;
using Katalog.Media;

namespace Katalog.Testy;

/// <summary>Recognising titles from file and folder names (invented titles only).</summary>
public sealed class RozpoznanieTesty
{
    [Test]
    public void KniznicaUniversumFilm()
    {
        var n = Navrh(@"MOVIES\Vymyslený Šťastný film (2024) [tmdbid-123456]", "Vymyslený Šťastný film (2024) - CZ EN FHD H264 Directors Cut SMALL.mkv");
        Over.Rovne(DruhTitulu.Film, n.Druh, "film");
        Over.Rovne("Vymyslený Šťastný film", n.Nazov, "názov z priečinka");
        Over.Rovne<int?>(2024, n.Rok, "rok");
        Over.Rovne<int?>(123456, n.TmdbId, "TMDB id");
        Over.Rovne(IstotaNavrhu.Isty, n.Istota, "knižnica UNIVERSUM je istá");
        Over.Postupnost(["cs", "en"], n.Jazyky, "jazyky zvuku");
        Over.Rovne(TriedaKvality.FHD, n.Kvalita, "kvalita");
        Over.Rovne("Directors Cut", n.Doplnok, "doplnok k názvu");
        Over.Pravda(!n.Extra, "nie je extra");

        var bezId = Navrh(@"MOVIES\Vymyslená komédia (1999)", "Vymyslená komédia (1999) - SK titulky EN HD H264 BIG2.mkv");
        Over.Pravda(bezId is { Nazov: "Vymyslená komédia", Rok: 1999, TmdbId: null, Istota: IstotaNavrhu.Isty }, "bez id stále istý");
        Over.Postupnost(["en"], bezId.Jazyky, "SK titulky nie sú zvuk");
        Over.Rovne<string?>(null, bezId.Doplnok, "číslo verzie a trieda veľkosti nie sú doplnok");
    }

    [Test]
    public void KniznicaUniversumSerial()
    {
        var n = Navrh(@"TVSHOWS\Vymyslený seriál (2025) [tmdbid-77]\Season 01", "Vymyslený seriál S01E04 - CZ FHD H265 SMALL.mkv");
        Over.Pravda(n is { Druh: DruhTitulu.Serial, Nazov: "Vymyslený seriál", Rok: 2025, TmdbId: 77, Seria: 1, Istota: IstotaNavrhu.Isty }, "epizóda");
        Over.Postupnost([4], n.Epizody, "epizóda 4");
        var dve = Navrh(@"TVSHOWS\Vymyslený seriál (2025)\Season 00", "Vymyslený seriál S00E01E02 - EN SD SMALL.mkv");
        Over.Pravda(dve is { Seria: 0 } && dve.Epizody.SequenceEqual([1, 2]), "špeciál s dvoma epizódami");
        var extra = Navrh(@"TVSHOWS\Vymyslený seriál (2025)\Extras", "Ako sa nakrúcalo.mkv");
        Over.Pravda(extra.Extra, "priečinok Extras");
    }

    [Test]
    public void BezneNazvyFilmov()
    {
        var release = Navrh(@"Stiahnuté", "Vymysleny.Hviezdny.Strazca.2019.1080p.BluRay.x264.CZ.dabing-SKUPINA.mkv");
        Over.Pravda(release is { Druh: DruhTitulu.Film, Nazov: "Vymysleny Hviezdny Strazca", Rok: 2019, Istota: IstotaNavrhu.Pravdepodobny }, $"release: {release}");
        Over.Rovne(TriedaKvality.FHD, release.Kvalita, "1080p");
        Over.Postupnost(["cs"], release.Jazyky, "CZ dabing");

        var cislo = Navrh(@"Filmy", "Vymyslený Bežec 2049 (2017).mkv");
        Over.Pravda(cislo is { Nazov: "Vymyslený Bežec 2049", Rok: 2017 }, $"číslo v názve nie je rok: {cislo}");
        var zaciatok = Navrh(@"Filmy", "1917 Vymyslená vojna (2019).mkv");
        Over.Pravda(zaciatok is { Nazov: "1917 Vymyslená vojna", Rok: 2019 }, $"rok na začiatku je názov: {zaciatok}");

        var priecinok = Navrh(@"Filmy\Vymyslený Strážca (2008)", "strazca-1080p.mkv");
        Over.Pravda(priecinok is { Nazov: "Vymyslený Strážca", Rok: 2008 }, $"rok z priečinka: {priecinok}");
        var bezRoka = Navrh(@"Filmy", "Vymyslený strážca.avi");
        Over.Pravda(bezRoka is { Nazov: "Vymyslený strážca", Rok: null, Istota: IstotaNavrhu.Neisty }, "bez roka neistý");

        var cd = Navrh(@"Filmy\Vymyslený epos (1975)", "Vymyslený epos (1975) CD2.avi");
        Over.Pravda(cd is { Nazov: "Vymyslený epos", Rok: 1975, Cast: 2 }, $"druhá časť: {cd}");

        var pokracovanie = Navrh(@"Filmy", "Vymyslený epos Part 2 (1976).mkv");
        Over.Pravda(pokracovanie is { Nazov: "Vymyslený epos Part 2", Cast: null }, $"Part 2 v názve nie je časť: {pokracovanie}");

        var tmdb = Navrh(@"Filmy", "Vymyslený film {tmdb-555}.mkv");
        Over.Pravda(tmdb is { TmdbId: 555, Istota: IstotaNavrhu.Isty, Nazov: "Vymyslený film" }, $"TMDB značka v súbore: {tmdb}");

        var ukazka = Navrh(@"Filmy\Vymyslený Strážca (2008)", "strazca-sample.mkv");
        Over.Pravda(ukazka.Extra, "ukážka");
        var bluray = RozpoznanieNazvu.Rozpoznaj(["Disky"], "Vymyslený Blu-ray (2012)", strukturaDisku: true)!;
        Over.Pravda(bluray is { Druh: DruhTitulu.Film, Nazov: "Vymyslený Blu-ray", Rok: 2012 }, "štruktúra disku podľa priečinka");
    }

    [Test]
    public void BezneNazvySerialov()
    {
        var release = Navrh(@"Seriály", "Vymysleny.Serial.S02E05.720p.WEB.mkv");
        Over.Pravda(release is { Druh: DruhTitulu.Serial, Nazov: "Vymysleny Serial", Seria: 2, Kvalita: TriedaKvality.HD } && release.Epizody.SequenceEqual([5]), $"release: {release}");
        var rozsah = Navrh(@"Seriály", "Vymyslený seriál S01E01-E03.mkv");
        Over.Postupnost([1, 2, 3], rozsah.Epizody, "rozsah epizód");
        var x = Navrh(@"Vymyslený seriál", "Vymyslený seriál 3x07.avi");
        Over.Pravda(x is { Seria: 3 } && x.Epizody.SequenceEqual([7]), "3x07");
        var dil = Navrh(@"Vymyslený seriál\Séria 2", "Díl 4.avi");
        Over.Pravda(dil is { Nazov: "Vymyslený seriál", Seria: 2 } && dil.Epizody.SequenceEqual([4]), $"Díl 4 v priečinku série: {dil}");
        var specialy = Navrh(@"Vymyslený seriál\Specials", "E01 Vianoce.mkv");
        Over.Pravda(specialy is { Seria: 0 }, "špeciály");
        var vysoke = Navrh(@"Vymyslený seriál\Season 1", "Vymyslený seriál - 1080p.mkv");
        Over.Pravda(vysoke.Druh == DruhTitulu.Film, "1080p nie je epizóda");
    }

    [Test]
    public void KlucTitulu()
    {
        var a = Navrh(@"Filmy", "Pán Prsteňov - Vymyslený návrat (2003).mkv");
        var b = Navrh(@"Iné", "Pan.Prstenov.Vymysleny.Navrat.2003.720p.mkv");
        Over.Rovne(a.Kluc, b.Kluc, "rovnaký titul pod rôznymi menami");
        Over.Rovne("spider man", Normalizacia.Nazov("Spider-Man"), "pomlčka");
        Over.Rovne("oceans eleven", Normalizacia.Nazov("Ocean's Eleven"), "apostrof");
    }

    private static NavrhTitulu Navrh(string priecinky, string subor) =>
        RozpoznanieNazvu.Rozpoznaj(priecinky.Split('\\', StringSplitOptions.RemoveEmptyEntries), subor)
        ?? throw new InvalidOperationException("nič sa nerozpoznalo: " + subor);
}
