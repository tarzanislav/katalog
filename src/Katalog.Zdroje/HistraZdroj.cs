using System.Globalization;
using System.Net.Http.Headers;
using System.Text.Json;
using Katalog.Jadro.Model;
using Katalog.Jadro.Rozhrania;

namespace Katalog.Zdroje;

/// <summary>A list of the user's in HISTRA as its overview gives it.</summary>
public sealed record ZoznamHistra(int Id, string Nazov, int Pocet, bool Moj);

/// <summary>
/// A title of a HISTRA list: the title as HISTRA names it ("movie/123"), its TMDB id when HISTRA gives one, and the data
/// HISTRA tells about it (shown in the title's detail, after TMDB's).
/// </summary>
public sealed record PolozkaHistra(Kandidat Titul, int? TmdbId, UdajeTitulu Udaje);

/// <summary>
/// HISTRA (histra.net) with the user's own token, as UNIVERSUM uses it – only reading: the catalogue is searched for a
/// title (it finds the TMDB id of a title, also without a TMDB key) and the user's lists are read on request. Only the
/// searched name is sent; requests go only to histra.net over HTTPS, at most 2 a second.
/// </summary>
public sealed class HistraZdroj(HttpClient http, string? token) : IZdrojUdajov
{
    public static readonly Uri Adresa = new("https://histra.net/");

    /// <summary>Where the user makes a token.</summary>
    public static readonly Uri StrankaTokenu = new("https://histra.net/settings/tracking-api");

    private const int Strana = 500;
    private const int NajviacPoloziek = 50_000;
    private const string Jazyk = "cs-CZ";

    private readonly string _token = token?.Trim() ?? "";

    public ZdrojUdajov Zdroj => ZdrojUdajov.Histra;

    public bool JeZapnuty => JePlatnyToken(_token);

    /// <summary>
    /// The token as it may go into a Bearer header (RFC 6750 token68): letters, digits and - . _ ~ + /, then '=' padding;
    /// anything else (spaces, line breaks, quotes) is refused, so the token can never change the request.
    /// </summary>
    public static bool JePlatnyToken(string? token)
    {
        if (token is not { Length: >= 8 and <= 4096 })
        {
            return false;
        }

        var koniec = token.Length;
        while (koniec > 0 && token[koniec - 1] == '=')
        {
            koniec--;
        }

        return koniec > 0 && token.AsSpan(0, koniec).IndexOfAnyExcept(Znaky) < 0;
    }

    /// <summary>
    /// Titles of the catalogue by name, those of the year first. The candidates are TMDB titles (the catalogue gives their
    /// TMDB ids): with a TMDB key their data come from TMDB, without it the name and year are written.
    /// </summary>
    public async Task<IReadOnlyList<Kandidat>> HladajAsync(HladanieTitulu hladanie, CancellationToken zrusenie)
    {
        if (!JeZapnuty || string.IsNullOrWhiteSpace(hladanie.Nazov))
        {
            return [];
        }

        var vysledok = new List<Kandidat>();
        foreach (var druh in hladanie.Druh is { } d ? [d] : new[] { DruhTitulu.Film, DruhTitulu.Serial })
        {
            var cesta = (druh == DruhTitulu.Film ? "api/v1/catalog/movies/search?q=" : "api/v1/catalog/shows/search?q=") + Uri.EscapeDataString(hladanie.Nazov.Trim());
            using var dokument = await ZiskajAsync(cesta, zrusenie).ConfigureAwait(false);
            vysledok.AddRange(CitajKatalog(dokument, druh));
        }

        return vysledok;
    }

    /// <summary>HISTRA's data of a title come with the lists (<see cref="CitajZoznamAsync"/>), not one by one.</summary>
    public Task<UdajeTitulu?> NacitajAsync(string identifikator, CancellationToken zrusenie) => Task.FromResult<UdajeTitulu?>(null);

    public Task<IReadOnlyList<UdajeKolekcie>> KolekcieAsync(string identifikator, CancellationToken zrusenie) =>
        Task.FromResult<IReadOnlyList<UdajeKolekcie>>([]);

    /// <summary>The user's lists.</summary>
    public async Task<IReadOnlyList<ZoznamHistra>> ZoznamyAsync(CancellationToken zrusenie)
    {
        var vysledok = new List<ZoznamHistra>();
        for (var od = 0; vysledok.Count < 5_000;)
        {
            using var dokument = await ZiskajAsync($"api/v1/lists?limit={Strana}&offset={od}", zrusenie).ConfigureAwait(false);
            var strana = CitajZoznamy(dokument);
            var nove = strana.Where(z => vysledok.All(v => v.Id != z.Id)).ToList();
            vysledok.AddRange(nove);
            if (nove.Count == 0 || strana.Count < Strana)
            {
                break;
            }

            od += strana.Count;
        }

        return vysledok;
    }

    /// <summary>All titles of one list (read in pages), with the data HISTRA gives about them.</summary>
    public async Task<IReadOnlyList<PolozkaHistra>> CitajZoznamAsync(int id, IProgress<int>? priebeh, CancellationToken zrusenie)
    {
        var vysledok = new List<PolozkaHistra>();
        var videne = new HashSet<string>(StringComparer.Ordinal);
        for (var od = 0; vysledok.Count < NajviacPoloziek;)
        {
            using var dokument = await ZiskajAsync(
                $"api/v1/lists/{id.ToString(CultureInfo.InvariantCulture)}?extended=full&lang={Jazyk}&limit={Strana}&offset={od}", zrusenie).ConfigureAwait(false);
            if (dokument is null)
            {
                break;
            }

            var polozky = Web.Pole(dokument.RootElement, "items").ToList();
            var pridane = 0;
            foreach (var polozka in polozky.Select(CitajPolozku).OfType<PolozkaHistra>())
            {
                if (videne.Add(polozka.Titul.Identifikator))
                {
                    vysledok.Add(polozka);
                    pridane++;
                }
            }

            priebeh?.Report(vysledok.Count);
            var pocet = Web.Cislo(Web.Objekt(dokument.RootElement, "list"), "item_count") ?? 0;
            if (polozky.Count == 0 || pridane == 0 || vysledok.Count >= pocet)
            {
                break;
            }

            od += polozky.Count;
        }

        return vysledok;
    }

    // ------------------------------------------------------------------ reading the answers (internal for tests)

    internal static List<Kandidat> CitajKatalog(JsonDocument? dokument, DruhTitulu druh)
    {
        var vysledok = new List<Kandidat>();
        if (dokument is null)
        {
            return vysledok;
        }

        foreach (var r in Web.Pole(dokument.RootElement, "results").Take(10))
        {
            if (Web.Cislo(Web.Objekt(r, "external_ids"), "tmdb") is not { } tmdb || tmdb <= 0)
            {
                continue;
            }

            var nazov = Web.Text(r, "title");
            var original = Web.Text(r, "original_title") ?? nazov;
            if (original is null)
            {
                continue;
            }

            vysledok.Add(new Kandidat(ZdrojUdajov.Tmdb, TmdbZdroj.Identifikator(druh, tmdb), druh, nazov ?? original, original,
                Web.Rok(Web.Text(r, druh == DruhTitulu.Film ? "release_date" : "first_air_date")), 0, TmdbZdroj.Plagat(Web.Text(r, "poster_path"))));
        }

        return vysledok;
    }

    internal static List<ZoznamHistra> CitajZoznamy(JsonDocument? dokument)
    {
        if (dokument is null)
        {
            return [];
        }

        var koren = dokument.RootElement;
        var pole = koren.ValueKind == JsonValueKind.Array ? koren.EnumerateArray().Where(e => e.ValueKind == JsonValueKind.Object).ToList() : Web.Pole(koren, "lists").ToList();
        return pole
            .Where(z => Web.Cislo(z, "id") is > 0)
            .Select(z => new ZoznamHistra(Web.Cislo(z, "id")!.Value, Web.Cisty(Web.Text(z, "name"), 200) ?? "Zoznam " + Web.Cislo(z, "id"),
                Math.Max(0, Web.Cislo(z, "item_count") ?? 0), z.TryGetProperty("is_mine", out var m) && m.ValueKind == JsonValueKind.True))
            .ToList();
    }

    internal static PolozkaHistra? CitajPolozku(JsonElement e)
    {
        DruhTitulu? druh = Web.Text(e, "media_type") switch
        {
            "movie" => DruhTitulu.Film,
            "show" or "tv" or "series" => DruhTitulu.Serial,
            _ => null,
        };
        var nazov = Web.Cisty(Web.Text(e, "title"), 300);
        var original = Web.Cisty(Web.Text(e, "original_title"), 300);
        if (druh is not { } d || Web.Cislo(e, "media_id") is not { } id || id <= 0 || (nazov ?? original) is not { } meno)
        {
            return null;
        }

        var externe = Web.Objekt(e, "external_ids");
        int? tmdb = Web.Cislo(externe, "tmdb") is int t and > 0 ? t : null;
        int? rok = Web.Cislo(e, "year") is int r and >= 1870 and <= 2200 ? r : null;
        var identifikator = (d == DruhTitulu.Film ? "movie/" : "tv/") + id.ToString(CultureInfo.InvariantCulture);
        var dalsie = new List<(ZdrojUdajov, string)>();
        if (tmdb is { } tm)
        {
            dalsie.Add((ZdrojUdajov.Tmdb, TmdbZdroj.Identifikator(d, tm)));
        }

        if (Web.Text(externe, "imdb") is { } imdb && imdb.StartsWith("tt", StringComparison.Ordinal) && imdb.Length is >= 7 and <= 12 && imdb[2..].All(char.IsAsciiDigit))
        {
            dalsie.Add((ZdrojUdajov.Imdb, imdb));
        }

        var plagat = TmdbZdroj.Plagat(Web.Text(e, "poster_path"));
        var udaje = new UdajeTitulu(ZdrojUdajov.Histra, identifikator, d, meno, original, rok, Web.Cisty(Web.Text(e, "overview")),
            Web.Cislo(e, "runtime") is int minuty and > 0 and < 10_000 ? minuty : null,
            Web.Desatinne(e, "rating") is double h and >= 0 and <= 10 ? Math.Round(h, 1) : null, [], [], [], plagat, dalsie);
        return new PolozkaHistra(new Kandidat(ZdrojUdajov.Histra, identifikator, d, meno, original, rok, 1, plagat), tmdb, udaje);
    }

    private Task<JsonDocument?> ZiskajAsync(string cesta, CancellationToken zrusenie)
    {
        if (!JeZapnuty)
        {
            throw new ChybaZdroja("Token HISTRA nie je platný – skontroluj ho v Nastaveniach.");
        }

        return Web.JsonAsync(http, () =>
        {
            if (!Uri.TryCreate(cesta, UriKind.Relative, out var relativna) || cesta.StartsWith("//", StringComparison.Ordinal) || cesta.Contains('\\'))
            {
                throw new ArgumentException("Cesta HISTRA musí byť relatívna.", nameof(cesta));
            }

            var poziadavka = new HttpRequestMessage(HttpMethod.Get, new Uri(Adresa, relativna));
            poziadavka.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _token);
            poziadavka.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            return poziadavka;
        }, Rozostup.Histra, "HISTRA", a => a.Scheme == Uri.UriSchemeHttps && a.Host == Adresa.Host && a.IsDefaultPort, zrusenie);
    }

    private static readonly System.Buffers.SearchValues<char> Znaky =
        System.Buffers.SearchValues.Create("ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789-._~+/");
}
