using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using Katalog.Jadro;
using Katalog.Jadro.Aktualizacie;

namespace Katalog.Okno;

/// <summary>
/// Talks to GitHub about releases: the latest release, and the download of its installer, which is started only when its
/// SHA-256 matches the checksum list signed by the release key. Only public addresses of GitHub are asked; nothing about
/// the user is sent (the request names only the program and its version).
/// </summary>
internal sealed class SluzbaAktualizacii : IDisposable
{
    private readonly HttpClient _http = new(new HttpClientHandler { AllowAutoRedirect = true, MaxAutomaticRedirections = 5 })
    {
        Timeout = TimeSpan.FromMinutes(5),
    };

    public SluzbaAktualizacii() =>
        _http.DefaultRequestHeaders.UserAgent.ParseAdd($"{Produkt.Nazov}/{Produkt.VerziaText}");

    /// <summary>The latest release, or null when none is published yet.</summary>
    public async Task<Vydanie?> PosledneAsync(CancellationToken zrusenie)
    {
        using var poziadavka = new HttpRequestMessage(HttpMethod.Get, Vydanie.PoslednePodlaApi);
        poziadavka.Headers.Accept.ParseAdd("application/vnd.github+json");
        using var odpoved = await _http.SendAsync(poziadavka, zrusenie).ConfigureAwait(false);
        if (odpoved.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        odpoved.EnsureSuccessStatusCode();
        return Vydanie.Citaj(await odpoved.Content.ReadAsStringAsync(zrusenie).ConfigureAwait(false));
    }

    /// <summary>Downloads the installer of <paramref name="vydanie"/>, checks it and returns its path in the temp folder.</summary>
    public async Task<string> StiahniAOverAsync(Vydanie vydanie, CancellationToken zrusenie)
    {
        if (!vydanie.DaSaNainstalovat)
        {
            throw new InvalidDataException("Vydanie nie je podpísané kľúčom vydaní.");
        }

        var zoznam = await StiahniAsync(vydanie.ZoznamSuctov!, zrusenie).ConfigureAwait(false);
        var podpis = await StiahniAsync(vydanie.Podpis!, zrusenie).ConfigureAwait(false);
        var ocakavany = PodpisVydania.OcakavanySucetInstalatora(zoznam, podpis, vydanie.Verzia)
                        ?? throw new InvalidDataException("Podpis vydania nesedí: aktualizácia sa nenainštaluje.");
        var instalator = await StiahniAsync(vydanie.Instalator!, zrusenie).ConfigureAwait(false);
        var skutocny = Convert.ToHexString(SHA256.HashData(instalator));
        if (!string.Equals(skutocny, ocakavany, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("Stiahnutý inštalátor sa líši od podpísaného: aktualizácia sa nenainštaluje.");
        }

        var priecinok = Path.Combine(Path.GetTempPath(), Produkt.Nazov + "-aktualizacia");
        Directory.CreateDirectory(priecinok);
        var cesta = Path.Combine(priecinok, PodpisVydania.NazovInstalatora(vydanie.Verzia));
        await File.WriteAllBytesAsync(cesta, instalator, zrusenie).ConfigureAwait(false);
        return cesta;
    }

    public void Dispose() => _http.Dispose();

    private async Task<byte[]> StiahniAsync(Uri adresa, CancellationToken zrusenie)
    {
        if (!Vydanie.JeOficialnaAdresa(adresa))
        {
            throw new InvalidDataException("Súbor vydania nie je na GitHube: " + adresa.Host);
        }

        using var odpoved = await _http.GetAsync(adresa, zrusenie).ConfigureAwait(false);
        odpoved.EnsureSuccessStatusCode();
        // the end of every redirect must be GitHub too
        if (odpoved.RequestMessage?.RequestUri is not { } konecna || !Vydanie.JeOficialnaAdresa(konecna))
        {
            throw new InvalidDataException("Súbor vydania nie je na GitHube.");
        }

        return await odpoved.Content.ReadAsByteArrayAsync(zrusenie).ConfigureAwait(false);
    }
}
