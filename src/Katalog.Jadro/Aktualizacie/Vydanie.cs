using System.Text.Json;

namespace Katalog.Jadro.Aktualizacie;

/// <summary>A published release of KATALOG on GitHub.</summary>
/// <param name="Poznamky">What is new (Slovak, written in the release).</param>
/// <param name="Instalator">KATALOG_Setup_1.2.3.exe of the release.</param>
/// <param name="ZoznamSuctov">SHA256SUMS.txt of the release.</param>
/// <param name="Podpis">SHA256SUMS.txt.sig: signature of the checksum list by the release key.</param>
public sealed record Vydanie(Version Verzia, string Poznamky, Uri Stranka, Uri? Instalator, Uri? ZoznamSuctov, Uri? Podpis)
{
    /// <summary>GitHub API address of the latest release (drafts and pre-releases are never "latest").</summary>
    public static Uri PoslednePodlaApi { get; } = new($"https://api.github.com/repos/{Produkt.Repozitar}/releases/latest");

    /// <summary>Pages and downloads of this repository's releases.</summary>
    public static string PredponaVydani { get; } = $"https://github.com/{Produkt.Repozitar}/releases/";

    /// <summary>The program installs an update itself only when the release is signed by the release key.</summary>
    public bool DaSaNainstalovat => PodpisVydania.JeNastaveny && Instalator is not null && ZoznamSuctov is not null && Podpis is not null;

    public bool JeNovsieAko(Version aktualna) => Normalizuj(Verzia) > Normalizuj(aktualna);

    /// <summary>All files of a release must come from GitHub over HTTPS (redirects are checked with this too).</summary>
    public static bool JeOficialnaAdresa(Uri adresa) =>
        adresa.Scheme == Uri.UriSchemeHttps &&
        adresa.Host.ToLowerInvariant() is "github.com" or "api.github.com" or "objects.githubusercontent.com" or "release-assets.githubusercontent.com";

    /// <summary>Reads the GitHub API answer; null when it is not a usable release (no 1.2.3 tag, draft, pre-release).</summary>
    public static Vydanie? Citaj(string json)
    {
        using var dokument = JsonDocument.Parse(json);
        var koren = dokument.RootElement;
        if (Pravda(koren, "draft") || Pravda(koren, "prerelease") ||
            Text(koren, "tag_name") is not { } znacka || !Version.TryParse(znacka.TrimStart('v', 'V'), out var verzia) ||
            verzia.Build < 0 || verzia.Revision >= 0 ||
            Text(koren, "html_url") is not { } stranka || !stranka.StartsWith(PredponaVydani, StringComparison.Ordinal))
        {
            return null;
        }

        Uri? instalator = null, sucty = null, podpis = null;
        if (koren.TryGetProperty("assets", out var subory) && subory.ValueKind == JsonValueKind.Array)
        {
            foreach (var subor in subory.EnumerateArray())
            {
                // downloads only from this repository's releases
                if (Text(subor, "name") is not { } nazov || Text(subor, "browser_download_url") is not { } url ||
                    !url.StartsWith(PredponaVydani + "download/", StringComparison.Ordinal) || !Uri.TryCreate(url, UriKind.Absolute, out var adresa))
                {
                    continue;
                }

                if (nazov.Equals("SHA256SUMS.txt", StringComparison.OrdinalIgnoreCase))
                {
                    sucty = adresa;
                }
                else if (nazov.Equals("SHA256SUMS.txt.sig", StringComparison.OrdinalIgnoreCase))
                {
                    podpis = adresa;
                }
                else if (nazov == PodpisVydania.NazovInstalatora(verzia))
                {
                    instalator = adresa;
                }
            }
        }

        return new Vydanie(verzia, Text(koren, "body")?.Trim() ?? "", new Uri(stranka), instalator, sucty, podpis);
    }

    private static Version Normalizuj(Version v) => new(v.Major, Math.Max(v.Minor, 0), Math.Max(v.Build, 0));

    private static string? Text(JsonElement prvok, string vlastnost) =>
        prvok.TryGetProperty(vlastnost, out var hodnota) && hodnota.ValueKind == JsonValueKind.String ? hodnota.GetString() : null;

    private static bool Pravda(JsonElement prvok, string vlastnost) =>
        prvok.TryGetProperty(vlastnost, out var hodnota) && hodnota.ValueKind == JsonValueKind.True;
}

/// <summary>When the program checks for a new release by itself.</summary>
public static class PravidloAktualizacii
{
    public static TimeSpan Interval { get; } = TimeSpan.FromHours(24);

    /// <summary>At start and then once a day, unless switched off; a clock moved back also triggers a check.</summary>
    public static bool MaKontrolovat(bool automaticky, DateTime? poslednaUtc, DateTime terazUtc) =>
        automaticky && (poslednaUtc is not { } posledna || terazUtc - posledna >= Interval || posledna > terazUtc);
}
