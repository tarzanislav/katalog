namespace Katalog.Jadro.Model;

/// <summary>How sure the recognition of a title from names is.</summary>
public enum IstotaNavrhu
{
    /// <summary>Only a guess (a name without a year): the user confirms it, or a source finds it.</summary>
    Neisty = 0,

    /// <summary>A name with a year: a source confirms it when it finds exactly this title (R5).</summary>
    Pravdepodobny = 1,

    /// <summary>
    /// UNIVERSUM's library ("MOVIES\Názov (rok)", "TVSHOWS\…\Season 01") or a TMDB id in the name: exact without the
    /// internet.
    /// </summary>
    Isty = 2,
}

/// <summary>What a video is, as its file and folder names tell (stage E4): only a proposal until confirmed.</summary>
/// <param name="Nazov">The title's name as written in the name (cleaned of release words).</param>
/// <param name="TmdbId">"[tmdbid-603]" in a folder or file name.</param>
/// <param name="Seria">Season of an episode (0 = specials); null for a film.</param>
/// <param name="Epizody">Episode numbers in the file (several for "S01E01E02"); empty for a film.</param>
/// <param name="Cast">Part of a film split into files ("CD1", "Part 2"); null when not split.</param>
/// <param name="Jazyky">Languages of sound the name gives ("CZ EN" → cs, en).</param>
/// <param name="Doplnok">What tells versions apart ("Directors Cut"); null when nothing.</param>
/// <param name="Extra">A sample, trailer or extra – not a title of its own.</param>
public sealed record NavrhTitulu(
    DruhTitulu Druh,
    string Nazov,
    int? Rok,
    int? TmdbId,
    int? Seria,
    IReadOnlyList<int> Epizody,
    int? Cast,
    IReadOnlyList<string> Jazyky,
    TriedaKvality Kvalita,
    string? Doplnok,
    IstotaNavrhu Istota,
    bool Extra = false)
{
    /// <summary>
    /// The key under which proposals of one title meet: kind, name without diacritics, punctuation and letter case,
    /// and year (a TMDB id wins when known).
    /// </summary>
    public string Kluc => TmdbId is { } id ? $"{(int)Druh}:tmdb:{id}" : Normalizacia.Kluc(Druh, Nazov, Rok);
}

/// <summary>Comparing names of titles: "Pán prsteňov: Návrat kráľa" and "pan prstenov navrat krala" are one name.</summary>
public static class Normalizacia
{
    /// <summary>The key of a name of a title: kind, normalised name and year ("1:pan prstenov navrat krala:2003").</summary>
    public static string Kluc(DruhTitulu druh, string nazov, int? rok) =>
        $"{(int)druh}:{Nazov(nazov)}:{rok?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? ""}";

    public static string Nazov(string nazov)
    {
        var rozlozeny = nazov.ToLowerInvariant().Normalize(System.Text.NormalizationForm.FormD);
        var vysledok = new System.Text.StringBuilder(rozlozeny.Length);
        var medzera = false;
        foreach (var znak in rozlozeny)
        {
            var kategoria = System.Globalization.CharUnicodeInfo.GetUnicodeCategory(znak);
            if (kategoria == System.Globalization.UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            if (char.IsLetterOrDigit(znak))
            {
                if (medzera && vysledok.Length > 0)
                {
                    vysledok.Append(' ');
                }

                vysledok.Append(znak == 'ł' ? 'l' : znak == 'ø' ? 'o' : znak == 'ß' ? 's' : znak);
                medzera = false;
            }
            else if (znak != '\'' && znak != '’')
            {
                // "Spider-Man" and "Spider Man" meet; an apostrophe joins ("Ocean's" = "Oceans")
                medzera = true;
            }
        }

        return vysledok.ToString();
    }
}
