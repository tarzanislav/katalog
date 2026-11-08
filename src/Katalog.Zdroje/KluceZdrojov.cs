using System.Text.Json;

namespace Katalog.Zdroje;

/// <summary>Where the key of a source comes from.</summary>
public enum PovodKluca
{
    /// <summary>No key: the source is off.</summary>
    Ziadny,

    /// <summary>The user's own key in KATALOG (it wins over UNIVERSUM's).</summary>
    Vlastny,

    /// <summary>The key stored by UNIVERSUM for the same Windows account.</summary>
    Universum,
}

/// <summary>The key a source uses, and where it came from.</summary>
public sealed record KlucZdroja(string? Kluc, PovodKluca Povod)
{
    public static KlucZdroja Ziadny { get; } = new(null, PovodKluca.Ziadny);
}

/// <summary>
/// The keys of TMDB and HISTRA (owner, 8. 11. 2026): KATALOG's own key wins; without one it uses the key UNIVERSUM keeps
/// for the same Windows account (its settings.json, protected by Windows for that account only). KATALOG never writes
/// UNIVERSUM's settings.
/// </summary>
public static class KluceZdrojov
{
    /// <summary>UNIVERSUM's settings file of this Windows account (its own environment variable moves it, as in UNIVERSUM).</summary>
    public static string SuborUniversum =>
        Path.Combine(
            Environment.GetEnvironmentVariable("UNIVERSUM_SETTINGS_DIR") is { Length: > 0 } iny
                ? iny
                : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "UNIVERSUM"),
            "settings.json");

    /// <summary>
    /// The protected TMDB key and HISTRA token in UNIVERSUM's settings (still protected: <paramref name="odchran"/> reads
    /// them), null for each that is not there or cannot be read.
    /// </summary>
    public static (string? Tmdb, string? Histra) Universum(string subor, Func<string, string?> odchran)
    {
        try
        {
            if (!File.Exists(subor) || new FileInfo(subor).Length > 4 << 20)
            {
                return (null, null);
            }

            using var dokument = JsonDocument.Parse(File.ReadAllBytes(subor));
            var koren = dokument.RootElement;
            string? Citaj(string vlastnost) =>
                koren.ValueKind == JsonValueKind.Object && koren.TryGetProperty(vlastnost, out var v) && v.ValueKind == JsonValueKind.String &&
                v.GetString() is { Length: > 0 } chraneny && odchran(chraneny) is { } kluc && !string.IsNullOrWhiteSpace(kluc)
                    ? kluc.Trim()
                    : null;
            return (Citaj("TmdbKeyProtected"), Citaj("HistraTokenProtected"));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return (null, null);
        }
    }

    /// <summary>The key to use: the own one when there is one, else UNIVERSUM's.</summary>
    public static KlucZdroja Vyber(string? vlastny, string? zUniversa) =>
        !string.IsNullOrWhiteSpace(vlastny) ? new KlucZdroja(vlastny.Trim(), PovodKluca.Vlastny)
        : !string.IsNullOrWhiteSpace(zUniversa) ? new KlucZdroja(zUniversa.Trim(), PovodKluca.Universum)
        : KlucZdroja.Ziadny;
}
