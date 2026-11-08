using System.Globalization;
using System.Text.RegularExpressions;
using Katalog.Jadro.Model;

namespace Katalog.Jadro.Rozhrania;

/// <summary>
/// What the user wrote to identify a title: a TMDB identifier (or a link to its page), a director, a name and a year –
/// any of them. "Carrie 1976", "Carrie (1976) rezia:De Palma", "tmdb:1271", "https://www.themoviedb.org/tv/1973-24".
/// </summary>
public sealed partial record IdentifikaciaTitulu(string? Nazov, int? Rok, string? Rezia, string? TmdbIdentifikator)
{
    /// <param name="druh">The kind of the title, for a bare "tmdb:603" (a link names its own kind).</param>
    public static IdentifikaciaTitulu Citaj(string? text, DruhTitulu druh)
    {
        var t = (text ?? "").Trim();
        if (OdkazTmdb().Match(t) is { Success: true } odkaz)
        {
            return new IdentifikaciaTitulu(null, null, null, (odkaz.Groups[1].Value == "tv" ? "tv/" : "movie/") + odkaz.Groups[2].Value);
        }

        if (IdTmdb().Match(t) is { Success: true } id)
        {
            var vlastny = id.Groups[1].Value.ToLowerInvariant();
            var predpona = vlastny switch { "tv/" => "tv/", "movie/" => "movie/", _ => druh == DruhTitulu.Serial ? "tv/" : "movie/" };
            return new IdentifikaciaTitulu(null, null, null, predpona + id.Groups[2].Value);
        }

        // "rezia:Meno Priezvisko" (also réžia, director) – the rest of the text after it is the director's name
        string? rezia = null;
        if (VzorRezie().Match(t) is { Success: true } r)
        {
            rezia = r.Groups[1].Value.Trim().Trim('"').Trim();
            t = t[..r.Index].Trim();
        }

        int? rok = null;
        if (RokNaKonci().Match(t) is { Success: true } rk && rk.Index > 0 &&
            int.TryParse(rk.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var cislo) && cislo is >= 1870 and <= 2200)
        {
            rok = cislo;
            t = t[..rk.Index].Trim().TrimEnd('-', ',', '.').Trim();
        }

        return new IdentifikaciaTitulu(t.Length > 0 ? t : null, rok, string.IsNullOrEmpty(rezia) ? null : rezia, null);
    }

    [GeneratedRegex(@"themoviedb\.org/(movie|tv)/(\d+)", RegexOptions.IgnoreCase)]
    private static partial Regex OdkazTmdb();

    [GeneratedRegex(@"^tmdb\s*[:#]?\s*(tv/|movie/)?(\d+)$", RegexOptions.IgnoreCase)]
    private static partial Regex IdTmdb();

    [GeneratedRegex(@"(?:r[eé][zž]i[ae]|director|reziser|režisér)\s*:\s*(.+)$", RegexOptions.IgnoreCase)]
    private static partial Regex VzorRezie();

    [GeneratedRegex(@"[\s(]\(?(\d{4})\)?$")]
    private static partial Regex RokNaKonci();
}
