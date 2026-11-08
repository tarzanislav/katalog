using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Katalog.Jadro.Rozhrania;

namespace Katalog.Zdroje;

/// <summary>
/// Spaces the requests to one service: one at a time and at most <see cref="ZaSekundu"/> per second, however many jobs
/// ask; after "too many requests" everybody waits as long as the service asks. One per service for the whole program.
/// </summary>
public sealed class Rozostup(double zaSekundu)
{
    private readonly SemaphoreSlim _brana = new(1, 1);
    private readonly TimeSpan _interval = TimeSpan.FromSeconds(1 / zaSekundu);
    private DateTime _dalsiUtc = DateTime.MinValue;

    /// <summary>TMDB allows about 40 requests a second; KATALOG uses a tenth of it (as UNIVERSUM).</summary>
    public static Rozostup Tmdb { get; } = new(4);

    /// <summary>HISTRA publishes no limit: 2 a second (as UNIVERSUM).</summary>
    public static Rozostup Histra { get; } = new(2);

    /// <summary>The user's own Jellyfin server.</summary>
    public static Rozostup Jellyfin { get; } = new(8);

    /// <summary>Pictures from TMDB's image server.</summary>
    public static Rozostup Obrazky { get; } = new(8);

    public double ZaSekundu { get; } = zaSekundu;

    public async Task<T> SpustiAsync<T>(Func<Task<T>> poziadavka, CancellationToken zrusenie)
    {
        await _brana.WaitAsync(zrusenie).ConfigureAwait(false);
        try
        {
            var cakat = _dalsiUtc - DateTime.UtcNow;
            if (cakat > TimeSpan.Zero)
            {
                await Task.Delay(cakat, zrusenie).ConfigureAwait(false);
            }

            _dalsiUtc = DateTime.UtcNow + _interval;
            return await poziadavka().ConfigureAwait(false);
        }
        finally
        {
            _brana.Release();
        }
    }

    /// <summary>The service asked to slow down: no request starts before <paramref name="cas"/> passes.</summary>
    public void Pozastav(TimeSpan cas)
    {
        var dokedy = DateTime.UtcNow + cas;
        if (dokedy > _dalsiUtc)
        {
            _dalsiUtc = dokedy;
        }
    }
}

/// <summary>
/// Requests to the web services: only to the service's own address over HTTPS (a redirect elsewhere is refused), passing
/// problems waited out and tried again, answers limited in size, problems as <see cref="ChybaZdroja"/> in Slovak.
/// </summary>
internal static class Web
{
    /// <summary>The largest answer accepted (the services answer with small JSON documents).</summary>
    public const int NajviacBajtov = 8 << 20;

    private const int PokusyPriObmedzeni = 4;
    private const int PokusyPriChybeServera = 2;

    /// <summary>Tests shorten waiting (1 = real time).</summary>
    internal static double MierkaCakania { get; set; } = 1;

    /// <summary>
    /// GET of a JSON document; null when the service has nothing there (404). <paramref name="povolena"/> says whether an
    /// address belongs to the service (the request and any redirect).
    /// </summary>
    public static async Task<JsonDocument?> JsonAsync(
        HttpClient http, Func<HttpRequestMessage> poziadavka, Rozostup rozostup, string sluzba, Func<Uri, bool> povolena, CancellationToken zrusenie)
    {
        var bajty = await BajtyAsync(http, poziadavka, rozostup, sluzba, povolena, NajviacBajtov, zrusenie).ConfigureAwait(false);
        if (bajty is null || bajty.Length == 0)
        {
            return null;
        }

        try
        {
            return JsonDocument.Parse(bajty);
        }
        catch (JsonException ex)
        {
            throw new ChybaZdroja($"{sluzba} vrátil nečitateľnú odpoveď.", ex);
        }
    }

    /// <summary>GET of raw bytes (a picture) of at most <paramref name="najviac"/> bytes; null for 404.</summary>
    public static async Task<byte[]?> BajtyAsync(
        HttpClient http, Func<HttpRequestMessage> poziadavka, Rozostup rozostup, string sluzba, Func<Uri, bool> povolena, int najviac,
        CancellationToken zrusenie)
    {
        for (var pokus = 0; ; pokus++)
        {
            using var sprava = poziadavka();
            if (sprava.RequestUri is not { } adresa || !povolena(adresa))
            {
                throw new ChybaZdroja($"Požiadavka na {sluzba} by išla na inú adresu – neodoslala sa.");
            }

            HttpResponseMessage odpoved;
            try
            {
                odpoved = await rozostup.SpustiAsync(
                    () => http.SendAsync(sprava, HttpCompletionOption.ResponseHeadersRead, zrusenie), zrusenie).ConfigureAwait(false);
            }
            catch (HttpRequestException ex)
            {
                throw new ChybaZdroja($"{sluzba} nie je dostupný: {ex.Message}", ex);
            }
            catch (TaskCanceledException ex) when (!zrusenie.IsCancellationRequested)
            {
                throw new ChybaZdroja($"{sluzba} neodpovedá.", ex);
            }

            using (odpoved)
            {
                if (odpoved.RequestMessage?.RequestUri is { } konecna && !povolena(konecna))
                {
                    throw new ChybaZdroja($"{sluzba} presmeroval na inú adresu ({konecna.Host}) – odpoveď sa nepoužila.");
                }

                if (odpoved.StatusCode is HttpStatusCode.TooManyRequests or HttpStatusCode.ServiceUnavailable or HttpStatusCode.BadGateway
                    or HttpStatusCode.GatewayTimeout or HttpStatusCode.InternalServerError)
                {
                    var obmedzene = odpoved.StatusCode == HttpStatusCode.TooManyRequests;
                    if (pokus >= (obmedzene ? PokusyPriObmedzeni : PokusyPriChybeServera))
                    {
                        throw new ChybaZdroja(obmedzene
                            ? $"{sluzba} obmedzil počet požiadaviek ani po čakaní – skús to o niekoľko minút."
                            : $"{sluzba} má problém ({(int)odpoved.StatusCode}) – skús to neskôr.");
                    }

                    var cakanie = Cakanie(odpoved.Headers.RetryAfter, pokus);
                    rozostup.Pozastav(cakanie);
                    continue;
                }

                switch (odpoved.StatusCode)
                {
                    case HttpStatusCode.NotFound or HttpStatusCode.NoContent:
                        return null;
                    case HttpStatusCode.Unauthorized:
                        throw new ChybaZdroja($"Kľúč pre {sluzba} nie je platný – skontroluj ho v Nastaveniach.");
                    case HttpStatusCode.Forbidden:
                        throw new ChybaZdroja($"Kľúč pre {sluzba} na to nemá oprávnenie.");
                    case >= HttpStatusCode.OK and < HttpStatusCode.Ambiguous:
                        return await CitajAsync(odpoved, sluzba, najviac, zrusenie).ConfigureAwait(false);
                    default:
                        throw new ChybaZdroja($"{sluzba} odpovedal chybou {(int)odpoved.StatusCode}.");
                }
            }
        }
    }

    /// <summary>What the service asks (Retry-After), else 2, 4, 8, 16 s; at most a minute.</summary>
    internal static TimeSpan Cakanie(RetryConditionHeaderValue? retryAfter, int pokus)
    {
        var ziadane = retryAfter switch
        {
            { Delta: { } delta } => delta,
            { Date: { } datum } => datum - DateTimeOffset.UtcNow,
            _ => (TimeSpan?)null,
        };
        var cas = ziadane is { } z && z > TimeSpan.Zero ? z : TimeSpan.FromSeconds(2 << Math.Min(pokus, 3));
        return (cas > TimeSpan.FromMinutes(1) ? TimeSpan.FromMinutes(1) : cas) * MierkaCakania;
    }

    private static async Task<byte[]> CitajAsync(HttpResponseMessage odpoved, string sluzba, int najviac, CancellationToken zrusenie)
    {
        if (odpoved.Content.Headers.ContentLength > najviac)
        {
            throw new ChybaZdroja($"Odpoveď {sluzba} je neprimerane veľká – nepoužila sa.");
        }

        await using var prud = await odpoved.Content.ReadAsStreamAsync(zrusenie).ConfigureAwait(false);
        using var buffer = new MemoryStream();
        var kus = new byte[81920];
        int precitane;
        while ((precitane = await prud.ReadAsync(kus, zrusenie).ConfigureAwait(false)) > 0)
        {
            if (buffer.Length + precitane > najviac)
            {
                throw new ChybaZdroja($"Odpoveď {sluzba} je neprimerane veľká – nepoužila sa.");
            }

            buffer.Write(kus, 0, precitane);
        }

        return buffer.ToArray();
    }

    // ------------------------------------------------------------------ JSON

    public static string? Text(JsonElement e, string vlastnost) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(vlastnost, out var v) && v.ValueKind == JsonValueKind.String
            ? Cisty(v.GetString())
            : null;

    public static int? Cislo(JsonElement e, string vlastnost) =>
        e.ValueKind != JsonValueKind.Object || !e.TryGetProperty(vlastnost, out var v) ? null
        : v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var c) ? c
        : v.ValueKind == JsonValueKind.String && int.TryParse(v.GetString(), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out c) ? c
        : null;

    public static double? Desatinne(JsonElement e, string vlastnost) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(vlastnost, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetDouble(out var d) ? d : null;

    public static JsonElement Objekt(JsonElement e, string vlastnost) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(vlastnost, out var v) && v.ValueKind == JsonValueKind.Object ? v : default;

    public static IEnumerable<JsonElement> Pole(JsonElement e, string vlastnost) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(vlastnost, out var v) && v.ValueKind == JsonValueKind.Array
            ? v.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.Object).ToList()
            : [];

    /// <summary>The year of an ISO date ("2001-12-19").</summary>
    public static int? Rok(string? datum) =>
        datum is { Length: >= 4 } && int.TryParse(datum.AsSpan(0, 4), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var r) && r is >= 1870 and <= 2200
            ? r
            : null;

    public static DateOnly? Datum(string? datum) =>
        DateOnly.TryParseExact(datum, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var d) ? d : null;

    /// <summary>Text from the web without control characters and with at most <paramref name="najviac"/> characters.</summary>
    public static string? Cisty(string? text, int najviac = 4000)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var cisty = new string(text.Where(z => !char.IsControl(z) || z is '\n').ToArray()).Trim();
        return cisty.Length == 0 ? null : cisty.Length > najviac ? cisty[..najviac] : cisty;
    }
}
