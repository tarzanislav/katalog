using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text.Json;
using Katalog.Jadro.Model;
using Katalog.Jadro.Rozhrania;

namespace Katalog.Zdroje;

/// <summary>A film or an episode the user watched on the Jellyfin server, by its TMDB id (of the series for an episode).</summary>
public sealed record PozretieJellyfin(DruhTitulu Druh, int TmdbId, int? Seria, int? Cislo, DateTime? KedyUtc);

/// <summary>
/// The user's own Jellyfin server (optional): its collections (box sets) and what the user watched there. The address
/// and an API key come from Settings; the key goes only to that address, over HTTPS – plain HTTP only inside the home
/// network (a private address, localhost or a .local name).
/// </summary>
public sealed class JellyfinZdroj(HttpClient http, string? adresa, string? kluc, string? pouzivatel) : IZdrojUdajov
{
    private const int Strana = 500;

    private readonly Uri? _adresa = Over(adresa);
    private readonly string _kluc = kluc?.Trim() ?? "";
    private readonly string _pouzivatel = pouzivatel?.Trim() ?? "";
    private string? _pouzivatelId;

    public ZdrojUdajov Zdroj => ZdrojUdajov.Jellyfin;

    public bool JeZapnuty => _adresa is not null && _kluc.Length is >= 16 and <= 256 && _kluc.All(char.IsAsciiLetterOrDigit);

    /// <summary>
    /// The server's address as it may be used: https anywhere, http only inside the home network; nothing else, no user
    /// name or password in it. Null for an address that may not be used.
    /// </summary>
    public static Uri? Over(string? adresa)
    {
        if (string.IsNullOrWhiteSpace(adresa) || !Uri.TryCreate(adresa.Trim().TrimEnd('/') + "/", UriKind.Absolute, out var uri) ||
            uri.UserInfo.Length > 0 || uri.Query.Length > 0 || uri.Fragment.Length > 0)
        {
            return null;
        }

        return uri.Scheme == Uri.UriSchemeHttps || (uri.Scheme == Uri.UriSchemeHttp && JeDomaca(uri.Host)) ? uri : null;
    }

    /// <summary>A host inside the home network: localhost, a .local name, a private IPv4 or a local IPv6 address.</summary>
    public static bool JeDomaca(string host)
    {
        if (host.Equals("localhost", StringComparison.OrdinalIgnoreCase) || host.EndsWith(".local", StringComparison.OrdinalIgnoreCase) ||
            (!host.Contains('.', StringComparison.Ordinal) && !host.Contains(':', StringComparison.Ordinal) && host.Length > 0))
        {
            return true;
        }

        if (!IPAddress.TryParse(host.Trim('[', ']'), out var ip))
        {
            return false;
        }

        if (IPAddress.IsLoopback(ip))
        {
            return true;
        }

        if (ip.AddressFamily == AddressFamily.InterNetworkV6)
        {
            return ip.IsIPv6LinkLocal || ip.IsIPv6UniqueLocal || ip.IsIPv6SiteLocal;
        }

        var b = ip.GetAddressBytes();
        return b[0] == 10 || (b[0] == 172 && b[1] is >= 16 and <= 31) || (b[0] == 192 && b[1] == 168) || (b[0] == 169 && b[1] == 254);
    }

    public Task<IReadOnlyList<Kandidat>> HladajAsync(HladanieTitulu hladanie, CancellationToken zrusenie) =>
        Task.FromResult<IReadOnlyList<Kandidat>>([]);

    public Task<UdajeTitulu?> NacitajAsync(string identifikator, CancellationToken zrusenie) => Task.FromResult<UdajeTitulu?>(null);

    /// <summary>Jellyfin's collections are read all at once (<see cref="VsetkyKolekcieAsync"/>).</summary>
    public Task<IReadOnlyList<UdajeKolekcie>> KolekcieAsync(string identifikator, CancellationToken zrusenie) =>
        Task.FromResult<IReadOnlyList<UdajeKolekcie>>([]);

    /// <summary>All collections of the server with their parts that have a TMDB id.</summary>
    public async Task<IReadOnlyList<UdajeKolekcie>> VsetkyKolekcieAsync(CancellationToken zrusenie)
    {
        var pouzivatel = await PouzivatelAsync(zrusenie).ConfigureAwait(false);
        var vysledok = new List<UdajeKolekcie>();
        foreach (var kolekcia in await PolozkyAsync($"Users/{pouzivatel}/Items?IncludeItemTypes=BoxSet&Recursive=true&Fields=Overview", zrusenie).ConfigureAwait(false))
        {
            if (Web.Text(kolekcia, "Id") is not { } id || Web.Text(kolekcia, "Name") is not { } nazov)
            {
                continue;
            }

            var diely = (await PolozkyAsync($"Users/{pouzivatel}/Items?ParentId={Uri.EscapeDataString(id)}&Fields=ProviderIds,ProductionYear", zrusenie).ConfigureAwait(false))
                .Select(CitajDiel)
                .OfType<Kandidat>()
                .ToList();
            if (diely.Count > 0)
            {
                vysledok.Add(new UdajeKolekcie(ZdrojUdajov.Jellyfin, id, nazov, Web.Text(kolekcia, "Overview"), diely));
            }
        }

        return vysledok;
    }

    /// <summary>Films and episodes the user watched on the server.</summary>
    public async Task<IReadOnlyList<PozretieJellyfin>> PozreteAsync(CancellationToken zrusenie)
    {
        var pouzivatel = await PouzivatelAsync(zrusenie).ConfigureAwait(false);
        var polozky = await PolozkyAsync(
            $"Users/{pouzivatel}/Items?Recursive=true&IncludeItemTypes=Movie,Episode&Filters=IsPlayed&Fields=ProviderIds&EnableUserData=true", zrusenie).ConfigureAwait(false);
        var serie = polozky.Where(p => Web.Text(p, "Type") == "Episode").Select(p => Web.Text(p, "SeriesId")).OfType<string>().Distinct().ToList();
        var tmdbSerii = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var skupina in serie.Chunk(100))
        {
            foreach (var s in await PolozkyAsync($"Users/{pouzivatel}/Items?Ids={string.Join(",", skupina.Select(Uri.EscapeDataString))}&Fields=ProviderIds", zrusenie).ConfigureAwait(false))
            {
                if (Web.Text(s, "Id") is { } id && TmdbId(s) is { } tmdb)
                {
                    tmdbSerii[id] = tmdb;
                }
            }
        }

        return CitajPozretia(polozky, tmdbSerii);
    }

    // ------------------------------------------------------------------ reading the answers (internal for tests)

    internal static Kandidat? CitajDiel(JsonElement p)
    {
        DruhTitulu? druh = Web.Text(p, "Type") switch
        {
            "Movie" => DruhTitulu.Film,
            "Series" => DruhTitulu.Serial,
            _ => null,
        };
        return druh is { } d && TmdbId(p) is { } tmdb && Web.Text(p, "Name") is { } nazov
            ? new Kandidat(ZdrojUdajov.Tmdb, TmdbZdroj.Identifikator(d, tmdb), d, nazov, Web.Text(p, "OriginalTitle"), Web.Cislo(p, "ProductionYear"), 1)
            : null;
    }

    internal static List<PozretieJellyfin> CitajPozretia(IEnumerable<JsonElement> polozky, IReadOnlyDictionary<string, int> tmdbSerii)
    {
        var vysledok = new List<PozretieJellyfin>();
        foreach (var p in polozky)
        {
            var kedy = DateTime.TryParse(Web.Text(Web.Objekt(p, "UserData"), "LastPlayedDate"), CultureInfo.InvariantCulture,
                DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var cas) ? cas : (DateTime?)null;
            switch (Web.Text(p, "Type"))
            {
                case "Movie" when TmdbId(p) is { } film:
                    vysledok.Add(new PozretieJellyfin(DruhTitulu.Film, film, null, null, kedy));
                    break;
                case "Episode" when Web.Text(p, "SeriesId") is { } s && tmdbSerii.TryGetValue(s, out var serial) &&
                                    Web.Cislo(p, "ParentIndexNumber") is { } seria && Web.Cislo(p, "IndexNumber") is { } cislo:
                    vysledok.Add(new PozretieJellyfin(DruhTitulu.Serial, serial, seria, cislo, kedy));
                    break;
            }
        }

        return vysledok;
    }

    private static int? TmdbId(JsonElement p)
    {
        var id = Web.Objekt(p, "ProviderIds");
        return id.ValueKind == JsonValueKind.Object
            ? id.EnumerateObject().Where(v => v.Name.Equals("Tmdb", StringComparison.OrdinalIgnoreCase) && v.Value.ValueKind == JsonValueKind.String)
                .Select(v => int.TryParse(v.Value.GetString(), NumberStyles.None, CultureInfo.InvariantCulture, out var c) && c > 0 ? c : (int?)null)
                .FirstOrDefault()
            : null;
    }

    private async Task<string> PouzivatelAsync(CancellationToken zrusenie)
    {
        if (_pouzivatelId is { } znamy)
        {
            return znamy;
        }

        using var dokument = await ZiskajAsync("Users", zrusenie).ConfigureAwait(false);
        var pouzivatelia = dokument?.RootElement.ValueKind == JsonValueKind.Array
            ? dokument.RootElement.EnumerateArray().Select(u => (Id: Web.Text(u, "Id"), Meno: Web.Text(u, "Name"))).Where(u => u.Id is not null).ToList()
            : [];
        var vybrany = _pouzivatel.Length > 0
            ? pouzivatelia.FirstOrDefault(u => string.Equals(u.Meno, _pouzivatel, StringComparison.OrdinalIgnoreCase))
            : pouzivatelia.Count == 1 ? pouzivatelia[0] : default;
        if (vybrany.Id is null)
        {
            throw new ChybaZdroja(_pouzivatel.Length > 0
                ? $"Používateľ „{_pouzivatel}“ na serveri Jellyfin nie je."
                : "Server Jellyfin má viac používateľov – v Nastaveniach napíš, ktorý si ty.");
        }

        return _pouzivatelId = vybrany.Id;
    }

    private async Task<List<JsonElement>> PolozkyAsync(string cesta, CancellationToken zrusenie)
    {
        var vysledok = new List<JsonElement>();
        for (var od = 0; vysledok.Count < 200_000;)
        {
            using var dokument = await ZiskajAsync($"{cesta}&StartIndex={od}&Limit={Strana}", zrusenie).ConfigureAwait(false);
            if (dokument is null)
            {
                break;
            }

            var strana = Web.Pole(dokument.RootElement, "Items").Select(e => e.Clone()).ToList();
            vysledok.AddRange(strana);
            var spolu = Web.Cislo(dokument.RootElement, "TotalRecordCount") ?? 0;
            if (strana.Count == 0 || vysledok.Count >= spolu)
            {
                break;
            }

            od += strana.Count;
        }

        return vysledok;
    }

    private Task<JsonDocument?> ZiskajAsync(string cesta, CancellationToken zrusenie)
    {
        if (!JeZapnuty || _adresa is not { } zaklad)
        {
            throw new ChybaZdroja("Jellyfin nie je nastavený – v Nastaveniach zadaj adresu servera a kľúč API.");
        }

        return Web.JsonAsync(http, () =>
        {
            var poziadavka = new HttpRequestMessage(HttpMethod.Get, new Uri(zaklad, cesta));
            poziadavka.Headers.TryAddWithoutValidation("Authorization", $"MediaBrowser Client=\"{Jadro.Produkt.Nazov}\", Token=\"{_kluc}\"");
            poziadavka.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            return poziadavka;
        }, Rozostup.Jellyfin, "Jellyfin", a => a.Scheme == zaklad.Scheme && a.Host == zaklad.Host && a.Port == zaklad.Port, zrusenie);
    }
}
