using System.Net;
using System.Text;
using System.Text.Json;
using Katalog.Jadro.Model;
using Katalog.Jadro.Rozhrania;
using Katalog.Zdroje;

namespace Katalog.Testy;

/// <summary>Reading the answers of TMDB, HISTRA and Jellyfin (invented answers, nothing goes to the internet).</summary>
public sealed class ZdrojeTesty
{
    [Test]
    public void TmdbDetailFilmu()
    {
        using var d = JsonDocument.Parse("""
            {
              "id": 9001, "title": "The Invented Guardian", "original_title": "The Invented Guardian", "release_date": "2008-07-16",
              "overview": "An invented plot.", "runtime": 152, "vote_average": 8.52, "vote_count": 100, "poster_path": "/default.jpg",
              "genres": [ { "id": 1, "name": "dobrodružný" }, { "id": 2, "name": "Akčný" } ],
              "belongs_to_collection": { "id": 77, "name": "Invented Collection" },
              "credits": {
                "cast": [ { "id": 2, "name": "Vymyslený Druhý", "character": "Pomocník", "order": 1 },
                          { "id": 1, "name": "Vymyslený Prvý", "character": "Strážca", "order": 0 } ],
                "crew": [ { "id": 3, "name": "Vymyslený Režisér", "job": "Director" }, { "id": 4, "name": "Vymyslený Kameraman", "job": "Camera" } ]
              },
              "external_ids": { "imdb_id": "tt0000001" },
              "translations": { "translations": [
                { "iso_639_1": "en", "data": { "title": "", "overview": "An invented plot." } },
                { "iso_639_1": "cs", "data": { "title": "Vymyšlený strážce", "overview": "Český děj." } },
                { "iso_639_1": "sk", "data": { "title": "Vymyslený strážca", "overview": "" } } ] },
              "images": { "posters": [ { "iso_639_1": "en", "file_path": "/en.jpg" }, { "iso_639_1": "cs", "file_path": "/cs.jpg", "vote_average": 5 } ] }
            }
            """);
        var u = TmdbZdroj.CitajDetail(d.RootElement, DruhTitulu.Film, "movie/9001");
        Over.Pravda(u is { Nazov: "Vymyslený strážca", Original: "The Invented Guardian", Rok: 2008, DlzkaMinut: 152, Hodnotenie: 8.5 }, $"film: {u}");
        Over.Rovne("Český děj.", u.Dej, "dej po česky, keď slovenský nie je");
        Over.Postupnost(["Dobrodružný", "Akčný"], u.Zanre, "žánre s veľkým písmenom");
        Over.Postupnost(["Vymyslený Prvý Herec", "Vymyslený Druhý Herec", "Vymyslený Režisér Rezia"], u.Osoby.Select(o => $"{o.Meno} {o.Rola}"), "osoby v poradí");
        Over.Rovne(new Uri("https://image.tmdb.org/t/p/w342/cs.jpg"), u.Plagat, "český plagát");
        Over.Postupnost([(ZdrojUdajov.Imdb, "tt0000001")], u.DalsieIdentifikatory, "IMDb");
    }

    [Test]
    public void TmdbHladanieSerialAKolekcia()
    {
        using var hladanie = JsonDocument.Parse("""
            { "results": [ { "id": 5, "name": "Vymyslený seriál", "original_name": "Invented Show", "first_air_date": "2025-01-02", "poster_path": "/a.jpg" },
                           { "id": 0, "name": "Bez id" }, { "id": 6, "original_name": "Iba originál", "poster_path": "../zly.jpg" } ] }
            """);
        var kandidati = TmdbZdroj.CitajHladanie(hladanie, DruhTitulu.Serial);
        Over.Rovne(2, kandidati.Count, "bez id sa vynechá");
        Over.Pravda(kandidati[0] is { Identifikator: "tv/5", Nazov: "Vymyslený seriál", Rok: 2025 } && kandidati[0].Plagat!.AbsolutePath == "/t/p/w342/a.jpg", "seriál");
        Over.Pravda(kandidati[1] is { Nazov: "Iba originál", Plagat: null }, "zlá cesta plagátu sa nepoužije");

        using var seria = JsonDocument.Parse("""
            { "episodes": [ { "season_number": 1, "episode_number": 1, "name": "Pilot", "air_date": "2025-01-02" },
                            { "season_number": 1, "episode_number": 2, "name": "", "air_date": "" } ] }
            """);
        Over.Postupnost([new UdajeEpizody(1, 1, "Pilot", new DateOnly(2025, 1, 2), null), new UdajeEpizody(1, 2, null, null, null)],
            TmdbZdroj.CitajEpizody(seria.RootElement), "epizódy");

        using var kolekcia = JsonDocument.Parse("""
            { "id": 77, "name": "Invented Collection", "poster_path": "/k.jpg",
              "translations": { "translations": [ { "iso_639_1": "sk", "data": { "title": "Vymyslená kolekcia", "overview": "Opis." } } ] },
              "parts": [ { "id": 12, "title": "Diel 2", "release_date": "2010-01-01" }, { "id": 13, "title": "Ohlásený diel" },
                         { "id": 11, "title": "Diel 1", "release_date": "2008-07-16", "poster_path": "/d1.jpg" } ] }
            """);
        var k = TmdbZdroj.CitajKolekciu(kolekcia.RootElement)!;
        Over.Pravda(k is { Identifikator: "collection/77", Nazov: "Vymyslená kolekcia", Dej: "Opis." } && k.Plagat is not null, "kolekcia po slovensky");
        Over.Postupnost(["movie/11", "movie/12", "movie/13"], k.Diely.Select(x => x.Identifikator), "diely podľa premiéry, ohlásený na konci");
        Over.Pravda(TmdbZdroj.Rozloz("movie/603") == (DruhTitulu.Film, 603) && TmdbZdroj.Rozloz("person/1") is null && TmdbZdroj.Rozloz("tv/x") is null, "identifikátory");
    }

    [Test]
    public void HistraKatalogAZoznamy()
    {
        Over.Pravda(HistraZdroj.JePlatnyToken("abcDEF123-._~+/=="), "platný token");
        foreach (var zly in new[] { "kratky", "s medzerou 12345", "riadok\n12345678", "\"uvodzovky\"123", "abc==def12345" })
        {
            Over.Pravda(!HistraZdroj.JePlatnyToken(zly), "neplatný token: " + zly);
        }

        using var katalog = JsonDocument.Parse("""
            { "results": [ { "id": 10, "title": "Invented Film", "original_title": "Vymyslený film", "release_date": "2001-05-05",
                             "external_ids": { "tmdb": "601" } },
                           { "id": 11, "title": "Bez TMDB" } ] }
            """);
        var kandidat = HistraZdroj.CitajKatalog(katalog, DruhTitulu.Film).Single();
        Over.Pravda(kandidat is { Zdroj: ZdrojUdajov.Tmdb, Identifikator: "movie/601", Original: "Vymyslený film", Rok: 2001 }, "katalóg nájde titul TMDB");

        using var zoznamy = JsonDocument.Parse("""{ "lists": [ { "id": 42, "name": "Chcem vidieť", "item_count": 2, "is_mine": true }, { "id": 0 } ] }""");
        Over.Postupnost([new ZoznamHistra(42, "Chcem vidieť", 2, true)], HistraZdroj.CitajZoznamy(zoznamy), "zoznamy");

        using var polozka = JsonDocument.Parse("""
            { "media_type": "show", "media_id": 700, "title": "Vymyslený seriál", "original_title": "Invented Show", "year": 2022,
              "overview": "Opis.", "rating": 7.46, "runtime": 45, "poster_path": "/s.jpg", "external_ids": { "tmdb": 5, "imdb": "tt1234567" } }
            """);
        var p = HistraZdroj.CitajPolozku(polozka.RootElement)!;
        Over.Pravda(p.Titul is { Zdroj: ZdrojUdajov.Histra, Identifikator: "tv/700", Druh: DruhTitulu.Serial, Rok: 2022 } && p.TmdbId == 5, "položka zoznamu");
        Over.Pravda(p.Udaje is { Zdroj: ZdrojUdajov.Histra, Hodnotenie: 7.5, DlzkaMinut: 45, Dej: "Opis." }, "údaje HISTRA");
        Over.Postupnost([(ZdrojUdajov.Tmdb, "tv/5"), (ZdrojUdajov.Imdb, "tt1234567")], p.Udaje.DalsieIdentifikatory, "TMDB a IMDb z HISTRA");
    }

    [Test]
    public void JellyfinAdresyKolekcieAPozretia()
    {
        Over.Pravda(JellyfinZdroj.Over("https://jellyfin.example.org") is not null, "https kdekoľvek");
        Over.Pravda(JellyfinZdroj.Over("http://192.168.1.20:8096") is not null && JellyfinZdroj.Over("http://nas:8096") is not null, "http len doma");
        Over.Pravda(JellyfinZdroj.Over("http://jellyfin.example.org") is null, "http na internet nie (kľúč by šiel nešifrovane)");
        Over.Pravda(JellyfinZdroj.Over("https://meno:heslo@server") is null && JellyfinZdroj.Over("ftp://server") is null, "zlé adresy");

        using var diel = JsonDocument.Parse("""{ "Type": "Movie", "Name": "Vymyslený diel", "ProductionYear": 2003, "ProviderIds": { "Tmdb": "102", "Imdb": "tt7" } }""");
        Over.Pravda(JellyfinZdroj.CitajDiel(diel.RootElement) is { Identifikator: "movie/102", Rok: 2003 }, "diel kolekcie");

        using var pozrete = JsonDocument.Parse("""
            [ { "Type": "Movie", "ProviderIds": { "Tmdb": "601" }, "UserData": { "LastPlayedDate": "2026-10-01T20:00:00.0000000Z" } },
              { "Type": "Episode", "SeriesId": "abc", "ParentIndexNumber": 1, "IndexNumber": 4, "UserData": {} },
              { "Type": "Episode", "SeriesId": "neznamy", "ParentIndexNumber": 1, "IndexNumber": 1 } ]
            """);
        var pozretia = JellyfinZdroj.CitajPozretia(pozrete.RootElement.EnumerateArray().ToList(), new Dictionary<string, int> { ["abc"] = 5 });
        Over.Postupnost(
            [new PozretieJellyfin(DruhTitulu.Film, 601, null, null, new DateTime(2026, 10, 1, 20, 0, 0, DateTimeKind.Utc)), new PozretieJellyfin(DruhTitulu.Serial, 5, 1, 4, null)],
            pozretia, "pozretia");
    }

    [Test]
    public void RozmeryObrazkovAZhoda()
    {
        byte[] png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 13, (byte)'I', (byte)'H', (byte)'D', (byte)'R', 0, 0, 1, 0x56, 0, 0, 2, 0x01, 8, 2];
        Over.Rovne((342, 513), StahovanieObrazkov.Rozmery(png), "PNG");
        byte[] jpeg = [0xFF, 0xD8, 0xFF, 0xE0, 0, 4, 0, 0, 0xFF, 0xC0, 0, 17, 8, 0x02, 0x01, 0x01, 0x56, 3, 0, 0, 0, 0];
        Over.Rovne((342, 513), StahovanieObrazkov.Rozmery(jpeg), "JPEG");
        Over.Rovne((0, 0), StahovanieObrazkov.Rozmery("<html>"u8), "nie obrázok");

        var hladanie = new HladanieTitulu("Pán Prsteňov - Vymyslený návrat", 2003, DruhTitulu.Film);
        Kandidat K(string id, string nazov, int? rok) => new(ZdrojUdajov.Tmdb, id, DruhTitulu.Film, nazov, null, rok, 0);
        var (zoradeni, isty) = ZhodaKandidata.Vyber(hladanie,
            [K("movie/1", "Pan prstenov: Vymysleny navrat", 2003), K("movie/2", "Pán prsteňov – vymyslený návrat", 2004), K("movie/3", "Iný film", 2003)]);
        Over.Rovne("movie/1", isty?.Identifikator, "rovnaký názov a rok sa vezme sám");
        Over.Postupnost(["movie/1", "movie/2", "movie/3"], zoradeni.Select(k => k.Identifikator), "poradie podľa zhody");
        Over.Rovne<Kandidat?>(null, ZhodaKandidata.Vyber(hladanie with { Rok = null }, [K("movie/1", "Pán Prsteňov - Vymyslený návrat", 2003)]).Isty, "bez roka nikdy sám");
        Over.Rovne<Kandidat?>(null, ZhodaKandidata.Vyber(hladanie, [K("movie/1", "Pán Prsteňov - Vymyslený návrat", 2003), K("movie/9", "Pan Prstenov Vymysleny Navrat", 2003)]).Isty,
            "dva rovnaké – opýta sa");
    }

    [Test]
    public async Task WebOpakujeAStrazAdresu()
    {
        Web.MierkaCakania = 0.001;
        try
        {
            var poradie = new Queue<HttpResponseMessage>([
                new HttpResponseMessage(HttpStatusCode.TooManyRequests),
                new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("""{ "results": [ { "id": 1, "title": "Vymyslený", "release_date": "2001-01-01" } ] }""", Encoding.UTF8) },
            ]);
            var spracovane = new List<Uri>();
            using var http = new HttpClient(new FalosnyServer(r => { spracovane.Add(r.RequestUri!); return poradie.Dequeue(); }));
            var tmdb = new TmdbZdroj(http, "vymyslenykluc123");
            var kandidati = await tmdb.HladajAsync(new HladanieTitulu("Vymyslený", 2001, DruhTitulu.Film), CancellationToken.None);
            Over.Rovne("movie/1", kandidati.Single().Identifikator, "po čakaní sa podarí");
            Over.Pravda(spracovane.All(u => u.Host == "api.themoviedb.org" && u.Query.Contains("year=2001", StringComparison.Ordinal)), "len TMDB, s rokom");
            Over.Pravda(spracovane[0].Query.Contains("query=Vymyslen%C3%BD", StringComparison.Ordinal), "posiela sa len hľadaný názov");

            using var zly = new HttpClient(new FalosnyServer(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized)));
            var chyba = await Over.VyhodiAsync<ChybaZdroja>(() => new TmdbZdroj(zly, "zlykluc12345678").HladajAsync(new HladanieTitulu("x"), CancellationToken.None), "zlý kľúč");
            Over.Pravda(chyba.Message.Contains("Nastaveniach", StringComparison.Ordinal), "správa pre používateľa");

            // a picture only from TMDB's image server
            using var obrazky = new HttpClient(new FalosnyServer(_ => throw new InvalidOperationException("nemalo sa nič stiahnuť")));
            Over.Rovne<StiahnutyObrazok?>(null, await StahovanieObrazkov.StiahniAsync(obrazky, new Uri("https://example.org/a.jpg"), CancellationToken.None), "cudzia adresa");
        }
        finally
        {
            Web.MierkaCakania = 1;
        }
    }

    private sealed class FalosnyServer(Func<HttpRequestMessage, HttpResponseMessage> odpoved) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var o = odpoved(request);
            o.RequestMessage = request;
            return Task.FromResult(o);
        }
    }
}
