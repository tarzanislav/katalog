using System.Globalization;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;
using Katalog.Jadro.Model;
using Katalog.Jadro.Rozhrania;

namespace Katalog.Zdroje;

/// <summary>
/// TMDB (themoviedb.org) with the user's own key – an API key (v3) or a Read Access Token (sent as a Bearer token).
/// Only the searched name and year, and the ids of titles, are sent. Names and plots are taken in Slovak, else Czech,
/// else English; posters Slovak or Czech when TMDB has them. Requests go one at a time through <see cref="Rozostup.Tmdb"/>.
/// </summary>
public sealed partial class TmdbZdroj(HttpClient http, string? kluc) : IZdrojUdajov
{
    /// <summary>The notice TMDB requires in programs that use its API (shown in O programe).</summary>
    public const string Upozornenie = "This product uses the TMDB API but is not endorsed or certified by TMDB.";

    public static readonly Uri Adresa = new("https://api.themoviedb.org/3/");

    /// <summary>TMDB's image server; posters in the size w342 (about 30–60 kB).</summary>
    public static readonly Uri Obrazky = new("https://image.tmdb.org/t/p/w342");

    /// <summary>The languages of names and plots, the first known wins.</summary>
    private static readonly string[] Jazyky = ["sk", "cs", "en"];

    /// <summary>At most this many people of a title (actors in their order, then directors and writers).</summary>
    private const int NajviacHercov = 15;

    private readonly string _kluc = kluc?.Trim() ?? "";
    private readonly Dictionary<string, (DateTime Cas, int? Kolekcia)> _kolekcie = new(StringComparer.Ordinal);

    public ZdrojUdajov Zdroj => ZdrojUdajov.Tmdb;

    public bool JeZapnuty => _kluc.Length > 0;

    /// <summary>True for a Read Access Token (a JWT), false for a v3 API key.</summary>
    private bool Token => _kluc.Count(z => z == '.') == 2 && _kluc.Length > 60;

    public async Task<IReadOnlyList<Kandidat>> HladajAsync(HladanieTitulu hladanie, CancellationToken zrusenie)
    {
        if (!JeZapnuty || string.IsNullOrWhiteSpace(hladanie.Nazov))
        {
            return [];
        }

        var druhy = hladanie.Druh is { } d ? [d] : new[] { DruhTitulu.Film, DruhTitulu.Serial };
        var vysledok = new List<Kandidat>();
        foreach (var druh in druhy)
        {
            var cesta = (druh == DruhTitulu.Film ? "search/movie" : "search/tv") + "?language=sk-SK&include_adult=false&query=" +
                        Uri.EscapeDataString(hladanie.Nazov.Trim());
            var sRokom = hladanie.Rok is { } rok
                ? cesta + (druh == DruhTitulu.Film ? "&year=" : "&first_air_date_year=") + rok.ToString(CultureInfo.InvariantCulture)
                : null;
            using var dokument = await ZiskajAsync(sRokom ?? cesta, zrusenie).ConfigureAwait(false);
            var najdene = CitajHladanie(dokument, druh);
            if (najdene.Count == 0 && sRokom is not null)
            {
                // the year in a file name is often the year of the release, not of the premiere
                using var bezRoka = await ZiskajAsync(cesta, zrusenie).ConfigureAwait(false);
                najdene = CitajHladanie(bezRoka, druh);
            }

            vysledok.AddRange(najdene);
        }

        return vysledok;
    }

    /// <summary>
    /// Titles a director made (films they directed, series they created or directed): the person TMDB finds first for the
    /// name, with "Directing" as their trade when there are more. Empty when TMDB knows no such person.
    /// </summary>
    public async Task<IReadOnlyList<Kandidat>> FilmyRezisera(string meno, CancellationToken zrusenie)
    {
        if (!JeZapnuty || string.IsNullOrWhiteSpace(meno))
        {
            return [];
        }

        using var osoby = await ZiskajAsync("search/person?language=sk-SK&include_adult=false&query=" + Uri.EscapeDataString(meno.Trim()), zrusenie).ConfigureAwait(false);
        if (VyberRezisera(osoby) is not { } osoba)
        {
            return [];
        }

        using var tvorba = await ZiskajAsync($"person/{osoba.ToString(CultureInfo.InvariantCulture)}/combined_credits?language=sk-SK", zrusenie).ConfigureAwait(false);
        return CitajTvorbuRezisera(tvorba);
    }

    /// <summary>The id of the person found: the first one whose trade is directing, else the first one.</summary>
    internal static int? VyberRezisera(JsonDocument? dokument)
    {
        if (dokument is null)
        {
            return null;
        }

        var osoby = Web.Pole(dokument.RootElement, "results").ToList();
        var reziser = osoby.FirstOrDefault(o => Web.Text(o, "known_for_department") == "Directing");
        return Web.Cislo(reziser.ValueKind == JsonValueKind.Object ? reziser : osoby.FirstOrDefault(), "id");
    }

    /// <summary>The crew credits of a person as directing (films) or creating/directing (series), each title once.</summary>
    internal static List<Kandidat> CitajTvorbuRezisera(JsonDocument? dokument)
    {
        if (dokument is null)
        {
            return [];
        }

        return Web.Pole(dokument.RootElement, "crew")
            .Where(c => Web.Text(c, "job") is "Director" or "Creator" or "Series Director" || Web.Text(c, "department") == "Directing")
            .Select(c =>
            {
                var serial = Web.Text(c, "media_type") == "tv";
                var druh = serial ? DruhTitulu.Serial : DruhTitulu.Film;
                var nazov = serial ? Web.Text(c, "name") ?? Web.Text(c, "original_name") : Web.Text(c, "title") ?? Web.Text(c, "original_title");
                var original = serial ? Web.Text(c, "original_name") : Web.Text(c, "original_title");
                var datum = serial ? Web.Text(c, "first_air_date") : Web.Text(c, "release_date");
                return Web.Cislo(c, "id") is > 0 and var id && nazov is not null
                    ? new Kandidat(ZdrojUdajov.Tmdb, Identifikator(druh, id), druh, nazov, original, Web.Rok(datum), 0, Plagat(Web.Text(c, "poster_path")))
                    : null;
            })
            .OfType<Kandidat>()
            .DistinctBy(k => k.Identifikator)
            .ToList();
    }

    /// <summary>
    /// The posters TMDB has for a title ("movie/603", "tv/1399"), for the user to pick one: Slovak first, then Czech,
    /// English and those without text, the best voted first in each language; at most <see cref="NajviacPlagatov"/>.
    /// </summary>
    public async Task<IReadOnlyList<PlagatTitulu>> PlagatyAsync(string identifikator, CancellationToken zrusenie)
    {
        if (!JeZapnuty || Rozloz(identifikator) is null)
        {
            return [];
        }

        using var dokument = await ZiskajAsync(identifikator + "/images?include_image_language=sk,cs,en,null", zrusenie).ConfigureAwait(false);
        return CitajPlagaty(dokument);
    }

    /// <summary>At most this many posters are offered.</summary>
    public const int NajviacPlagatov = 40;

    internal static List<PlagatTitulu> CitajPlagaty(JsonDocument? dokument)
    {
        if (dokument is null)
        {
            return [];
        }

        string[] poradie = ["sk", "cs", "en"];
        return Web.Pole(dokument.RootElement, "posters")
            .Select(p => (Jazyk: Web.Text(p, "iso_639_1"), Hlasy: Web.Desatinne(p, "vote_average") ?? 0, Adresa: Plagat(Web.Text(p, "file_path")),
                Sirka: Web.Cislo(p, "width"), Vyska: Web.Cislo(p, "height")))
            .Where(p => p.Adresa is not null)
            .OrderBy(p => p.Jazyk is { } j && Array.IndexOf(poradie, j) is var i and >= 0 ? i : p.Jazyk is null ? poradie.Length : poradie.Length + 1)
            .ThenByDescending(p => p.Hlasy)
            .DistinctBy(p => p.Adresa!.AbsoluteUri)
            .Take(NajviacPlagatov)
            .Select(p => new PlagatTitulu(p.Adresa!, p.Jazyk, p.Sirka, p.Vyska))
            .ToList();
    }

    /// <summary>A title by its TMDB identifier ("movie/603", "tv/1399") as a candidate; null when TMDB does not know it.</summary>
    public async Task<Kandidat?> KandidatPodlaIdAsync(string identifikator, CancellationToken zrusenie)
    {
        if (!JeZapnuty || Rozloz(identifikator) is not { } rozlozeny)
        {
            return null;
        }

        using var dokument = await ZiskajAsync(identifikator + "?language=sk-SK", zrusenie).ConfigureAwait(false);
        if (dokument is null)
        {
            return null;
        }

        var d = dokument.RootElement;
        var serial = rozlozeny.Druh == DruhTitulu.Serial;
        var nazov = serial ? Web.Text(d, "name") ?? Web.Text(d, "original_name") : Web.Text(d, "title") ?? Web.Text(d, "original_title");
        return nazov is null
            ? null
            : new Kandidat(ZdrojUdajov.Tmdb, identifikator, rozlozeny.Druh, nazov, serial ? Web.Text(d, "original_name") : Web.Text(d, "original_title"),
                Web.Rok(serial ? Web.Text(d, "first_air_date") : Web.Text(d, "release_date")), 1, Plagat(Web.Text(d, "poster_path")));
    }

    public async Task<UdajeTitulu?> NacitajAsync(string identifikator, CancellationToken zrusenie)
    {
        if (!JeZapnuty || Rozloz(identifikator) is not { } rozlozeny)
        {
            return null;
        }

        var (druh, id) = rozlozeny;
        var cesta = $"{identifikator}?language=sk-SK&append_to_response=credits,external_ids,translations,images&include_image_language=sk,cs,en,null";
        using var dokument = await ZiskajAsync(cesta, zrusenie).ConfigureAwait(false);
        if (dokument is null)
        {
            return null;
        }

        var udaje = CitajDetail(dokument.RootElement, druh, identifikator);
        lock (_kolekcie)
        {
            _kolekcie[identifikator] = (DateTime.UtcNow, Web.Cislo(Web.Objekt(dokument.RootElement, "belongs_to_collection"), "id"));
        }

        if (druh == DruhTitulu.Serial)
        {
            var serie = Web.Pole(dokument.RootElement, "seasons").Select(s => Web.Cislo(s, "season_number")).OfType<int>().Where(s => s >= 0).Distinct().ToList();
            var epizody = new List<UdajeEpizody>();
            // up to 20 seasons in one request (TMDB's limit for appended answers)
            foreach (var skupina in serie.Chunk(20))
            {
                var pripojene = string.Join(",", skupina.Select(s => "season/" + s.ToString(CultureInfo.InvariantCulture)));
                using var serieDokument = await ZiskajAsync($"tv/{id.ToString(CultureInfo.InvariantCulture)}?language=sk-SK&append_to_response={pripojene}", zrusenie).ConfigureAwait(false);
                if (serieDokument is not null)
                {
                    foreach (var s in skupina)
                    {
                        if (serieDokument.RootElement.TryGetProperty("season/" + s.ToString(CultureInfo.InvariantCulture), out var seria))
                        {
                            epizody.AddRange(CitajEpizody(seria));
                        }
                    }
                }
            }

            udaje = udaje with { Epizody = epizody };
        }

        return udaje;
    }

    public async Task<IReadOnlyList<UdajeKolekcie>> KolekcieAsync(string identifikator, CancellationToken zrusenie)
    {
        if (!JeZapnuty || Rozloz(identifikator) is not { Druh: DruhTitulu.Film })
        {
            return [];
        }

        int? kolekcia;
        bool znama;
        lock (_kolekcie)
        {
            znama = _kolekcie.TryGetValue(identifikator, out var z) && DateTime.UtcNow - z.Cas < TimeSpan.FromMinutes(30);
            kolekcia = znama ? z.Kolekcia : null;
        }

        if (!znama)
        {
            using var film = await ZiskajAsync(identifikator + "?language=sk-SK", zrusenie).ConfigureAwait(false);
            kolekcia = film is null ? null : Web.Cislo(Web.Objekt(film.RootElement, "belongs_to_collection"), "id");
        }

        if (kolekcia is not { } k)
        {
            return [];
        }

        using var dokument = await ZiskajAsync($"collection/{k.ToString(CultureInfo.InvariantCulture)}?language=sk-SK&append_to_response=translations", zrusenie)
            .ConfigureAwait(false);
        return dokument is null ? [] : CitajKolekciu(dokument.RootElement) is { } u ? [u] : [];
    }

    // ------------------------------------------------------------------ reading the answers (internal for tests)

    internal static List<Kandidat> CitajHladanie(JsonDocument? dokument, DruhTitulu druh)
    {
        if (dokument is null)
        {
            return [];
        }

        var vysledok = new List<Kandidat>();
        foreach (var r in Web.Pole(dokument.RootElement, "results").Take(10))
        {
            if (Web.Cislo(r, "id") is not { } id || id <= 0)
            {
                continue;
            }

            var nazov = Web.Text(r, druh == DruhTitulu.Film ? "title" : "name");
            var original = Web.Text(r, druh == DruhTitulu.Film ? "original_title" : "original_name");
            if ((nazov ?? original) is not { } meno)
            {
                continue;
            }

            vysledok.Add(new Kandidat(ZdrojUdajov.Tmdb, Identifikator(druh, id), druh, meno, original,
                Web.Rok(Web.Text(r, druh == DruhTitulu.Film ? "release_date" : "first_air_date")), 0, Plagat(Web.Text(r, "poster_path"))));
        }

        return vysledok;
    }

    internal static UdajeTitulu CitajDetail(JsonElement d, DruhTitulu druh, string identifikator)
    {
        var film = druh == DruhTitulu.Film;
        var original = Web.Text(d, film ? "original_title" : "original_name");
        var preklady = Web.Pole(Web.Objekt(d, "translations"), "translations")
            .Select(p => (Jazyk: Web.Text(p, "iso_639_1"), Udaje: Web.Objekt(p, "data")))
            .Where(p => p.Jazyk is not null)
            .ToList();
        string? Preklad(string pole)
        {
            foreach (var jazyk in Jazyky)
            {
                if (preklady.FirstOrDefault(p => p.Jazyk == jazyk) is { Udaje.ValueKind: JsonValueKind.Object } p && Web.Text(p.Udaje, pole) is { } text)
                {
                    return text;
                }
            }

            return null;
        }

        var nazov = Preklad(film ? "title" : "name") ?? Web.Text(d, film ? "title" : "name") ?? original ?? identifikator;
        var dej = Preklad("overview") ?? Web.Text(d, "overview");
        var rok = Web.Rok(Web.Text(d, film ? "release_date" : "first_air_date"));
        int? dlzka = film ? Web.Cislo(d, "runtime") : null;
        if (!film && d.TryGetProperty("episode_run_time", out var dlzky) && dlzky.ValueKind == JsonValueKind.Array)
        {
            dlzka = dlzky.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.Number && x.TryGetInt32(out _)).Select(x => x.GetInt32()).FirstOrDefault(x => x > 0) is var m && m > 0 ? m : null;
        }

        var hodnotenie = Web.Desatinne(d, "vote_average") is { } h && h > 0 && (Web.Cislo(d, "vote_count") ?? 0) > 0 ? Math.Round(h, 1) : (double?)null;
        var zanre = Web.Pole(d, "genres").Select(g => Web.Text(g, "name")).OfType<string>().Select(PrvePismenoVelke).ToList();

        var osoby = new List<UdajeOsoby>();
        var credits = Web.Objekt(d, "credits");
        foreach (var herec in Web.Pole(credits, "cast").OrderBy(c => Web.Cislo(c, "order") ?? int.MaxValue).Take(NajviacHercov))
        {
            if (Web.Cislo(herec, "id") is { } id && Web.Text(herec, "name") is { } meno)
            {
                osoby.Add(new UdajeOsoby("person/" + id.ToString(CultureInfo.InvariantCulture), meno, RolaOsoby.Herec, Web.Text(herec, "character"), osoby.Count));
            }
        }

        var tvorcovia = film
            ? Web.Pole(credits, "crew").Where(c => Web.Text(c, "job") == "Director").Select(c => (Prvok: c, Rola: RolaOsoby.Rezia))
                .Concat(Web.Pole(credits, "crew").Where(c => Web.Text(c, "job") is "Screenplay" or "Writer").Select(c => (Prvok: c, Rola: RolaOsoby.Scenar)))
            : Web.Pole(d, "created_by").Select(c => (Prvok: c, Rola: RolaOsoby.Rezia));
        foreach (var (prvok, rola) in tvorcovia.Take(8))
        {
            if (Web.Cislo(prvok, "id") is { } id && Web.Text(prvok, "name") is { } meno &&
                osoby.All(o => o.Identifikator != "person/" + id.ToString(CultureInfo.InvariantCulture) || o.Rola != rola))
            {
                osoby.Add(new UdajeOsoby("person/" + id.ToString(CultureInfo.InvariantCulture), meno, rola, null, osoby.Count));
            }
        }

        // posters: Slovak, else Czech, else TMDB's own choice
        var plagaty = Web.Pole(Web.Objekt(d, "images"), "posters").ToList();
        var cesta = new[] { "sk", "cs" }
            .Select(j => plagaty.Where(p => Web.Text(p, "iso_639_1") == j).OrderByDescending(p => Web.Desatinne(p, "vote_average") ?? 0).Select(p => Web.Text(p, "file_path")).FirstOrDefault())
            .FirstOrDefault(p => p is not null) ?? Web.Text(d, "poster_path");

        var dalsie = new List<(ZdrojUdajov, string)>();
        var externe = Web.Objekt(d, "external_ids");
        if ((Web.Text(externe, "imdb_id") ?? Web.Text(d, "imdb_id")) is { } imdb && ImdbId().IsMatch(imdb))
        {
            dalsie.Add((ZdrojUdajov.Imdb, imdb));
        }

        return new UdajeTitulu(ZdrojUdajov.Tmdb, identifikator, druh, nazov, original, rok, dej, dlzka is > 0 ? dlzka : null, hodnotenie,
            zanre, osoby, [], Plagat(cesta), dalsie);
    }

    internal static List<UdajeEpizody> CitajEpizody(JsonElement seria)
    {
        var vysledok = new List<UdajeEpizody>();
        foreach (var e in Web.Pole(seria, "episodes"))
        {
            if (Web.Cislo(e, "season_number") is { } s && Web.Cislo(e, "episode_number") is { } c && s >= 0 && c >= 0)
            {
                vysledok.Add(new UdajeEpizody(s, c, Web.Text(e, "name"), Web.Datum(Web.Text(e, "air_date")), Web.Text(e, "overview")));
            }
        }

        return vysledok;
    }

    internal static UdajeKolekcie? CitajKolekciu(JsonElement k)
    {
        if (Web.Cislo(k, "id") is not { } id)
        {
            return null;
        }

        var preklady = Web.Pole(Web.Objekt(k, "translations"), "translations")
            .Select(p => (Jazyk: Web.Text(p, "iso_639_1"), Udaje: Web.Objekt(p, "data")))
            .ToList();
        string? Preklad(string pole) => Jazyky
            .Select(j => preklady.FirstOrDefault(p => p.Jazyk == j))
            .Where(p => p.Udaje.ValueKind == JsonValueKind.Object)
            .Select(p => Web.Text(p.Udaje, pole))
            .FirstOrDefault(t => t is not null);

        var nazov = Preklad("title") ?? Preklad("name") ?? Web.Text(k, "name");
        if (nazov is null)
        {
            return null;
        }

        var diely = Web.Pole(k, "parts")
            .Where(p => Web.Text(p, "media_type") is null or "movie")
            .Select(p => (Id: Web.Cislo(p, "id"), Nazov: Web.Text(p, "title") ?? Web.Text(p, "original_title"), Original: Web.Text(p, "original_title"),
                Datum: Web.Text(p, "release_date"), Plagat: Web.Text(p, "poster_path")))
            .Where(p => p.Id is > 0 && p.Nazov is not null)
            // in the order of their premieres; parts without a date (announced) at the end
            .OrderBy(p => p.Datum is null ? 1 : 0).ThenBy(p => p.Datum, StringComparer.Ordinal)
            .Select(p => new Kandidat(ZdrojUdajov.Tmdb, Identifikator(DruhTitulu.Film, p.Id!.Value), DruhTitulu.Film, p.Nazov!, p.Original, Web.Rok(p.Datum), 1,
                Plagat(p.Plagat)))
            .ToList();
        return new UdajeKolekcie(ZdrojUdajov.Tmdb, "collection/" + id.ToString(CultureInfo.InvariantCulture), nazov, Preklad("overview") ?? Web.Text(k, "overview"), diely)
        {
            Plagat = Plagat(Web.Text(k, "poster_path")),
        };
    }

    /// <summary>"movie/603" or "tv/1399".</summary>
    public static string Identifikator(DruhTitulu druh, int id) =>
        (druh == DruhTitulu.Film ? "movie/" : "tv/") + id.ToString(CultureInfo.InvariantCulture);

    internal static (DruhTitulu Druh, int Id)? Rozloz(string identifikator)
    {
        var casti = identifikator.Split('/');
        return casti.Length == 2 && int.TryParse(casti[1], NumberStyles.None, CultureInfo.InvariantCulture, out var id) && id > 0
            ? casti[0] switch
            {
                "movie" => (DruhTitulu.Film, id),
                "tv" => (DruhTitulu.Serial, id),
                _ => null,
            }
            : null;
    }

    /// <summary>The address of a poster path ("/abc123.jpg"); null for anything else.</summary>
    public static Uri? Plagat(string? cesta) =>
        cesta is not null && CestaObrazka().IsMatch(cesta) ? new Uri(Obrazky.AbsoluteUri + cesta) : null;

    private Task<JsonDocument?> ZiskajAsync(string cesta, CancellationToken zrusenie) =>
        Web.JsonAsync(http, () =>
        {
            var adresa = new Uri(Adresa, Token ? cesta : cesta + "&api_key=" + Uri.EscapeDataString(_kluc));
            var poziadavka = new HttpRequestMessage(HttpMethod.Get, adresa);
            if (Token)
            {
                poziadavka.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _kluc);
            }

            poziadavka.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            return poziadavka;
        }, Rozostup.Tmdb, "TMDB", a => a.Scheme == Uri.UriSchemeHttps && a.Host == Adresa.Host, zrusenie);

    private static string PrvePismenoVelke(string text) =>
        text.Length == 0 ? text : char.ToUpper(text[0], CultureInfo.GetCultureInfo("sk-SK")) + text[1..];

    [GeneratedRegex(@"^/[A-Za-z0-9_-]{1,100}\.(jpg|jpeg|png)$")]
    private static partial Regex CestaObrazka();

    [GeneratedRegex(@"^tt\d{5,10}$")]
    private static partial Regex ImdbId();
}

/// <summary>A poster a source offers for a title: its address (w342), language (null = without text) and full size.</summary>
public sealed record PlagatTitulu(Uri Adresa, string? Jazyk, int? Sirka, int? Vyska);
