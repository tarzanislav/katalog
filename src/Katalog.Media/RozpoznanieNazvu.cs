using System.Globalization;
using System.Text.RegularExpressions;
using Katalog.Jadro.Model;

namespace Katalog.Media;

/// <summary>
/// What a video is, read from its file and folder names (stage E4), without the internet. UNIVERSUM's library is exact:
/// "MOVIES\Názov (rok) [tmdbid-603]\Názov (rok) - CZ EN FHD H264 SMALL.mkv" and
/// "TVSHOWS\Seriál (rok)\Season 01\Seriál S01E04 - CZ FHD H265 SMALL.mkv". Other names ("Názov.1999.1080p.BluRay.x264-SKUPINA",
/// "Seriál.S02E05.WEB", "1x04", "Díl 4" in a season folder) give a proposal the sources or the user confirm (R5).
/// </summary>
public static partial class RozpoznanieNazvu
{
    /// <summary>Words of release names after the title: quality, source, codec, language, edition flags.</summary>
    private static readonly HashSet<string> Zastavky = new(
        [
            "2160p", "1080p", "1080i", "720p", "576p", "480p", "4k", "uhd", "fhd", "hd", "sd", "hdr", "hdr10", "hdr10+", "dv", "dovi", "sdr",
            "bluray", "blu-ray", "bdrip", "brrip", "bdremux", "remux", "web", "webrip", "web-dl", "webdl", "hdtv", "dvdrip", "dvd", "dvdscr",
            "hdrip", "tvrip", "x264", "x265", "h264", "h265", "h.264", "h.265", "hevc", "avc", "xvid", "divx", "av1", "vp9", "10bit", "8bit",
            "aac", "ac3", "eac3", "dts", "dts-hd", "truehd", "atmos", "ddp5.1", "dd5.1", "5.1", "7.1",
            "cz", "sk", "en", "cze", "slo", "eng", "czech", "slovak", "english", "dabing", "titulky", "multi", "dual",
            "proper", "repack", "extended", "unrated", "remastered", "internal", "limited", "complete", "imax",
            "small", "big",
        ],
        StringComparer.OrdinalIgnoreCase);

    /// <summary>Folders that hold extras of a title, not titles.</summary>
    private static readonly HashSet<string> PriecinkyExtra = new(
        [
            "sample", "samples", "extras", "extra", "bonus", "featurettes", "featurette", "trailers", "trailer", "behind the scenes",
            "deleted scenes", "interviews", "scenes", "shorts", "other", "ukazka", "ukážka", "ukážky", "bonusy",
        ],
        StringComparer.OrdinalIgnoreCase);

    /// <summary>Folders whose name says where films lie, not which film it is.</summary>
    private static readonly HashSet<string> VseobecnePriecinky = new(
        [
            "filmy", "film", "movies", "movie", "video", "videa", "videá", "videos", "downloads", "stiahnuté", "stiahnute", "torrent",
            "torrents", "new folder", "nový priečinok", "novy priecinok", "serialy", "seriály", "seriály", "series", "tv", "tvshows",
            "dokumenty", "dokumentárne", "rozpravky", "rozprávky", "zaloha", "záloha", "backup", "hotove", "hotové",
        ],
        StringComparer.OrdinalIgnoreCase);

    /// <summary>Languages of sound as the names write them.</summary>
    private static readonly Dictionary<string, string> JazykyNazvov = new(StringComparer.OrdinalIgnoreCase)
    {
        ["cz"] = "cs", ["cze"] = "cs", ["czech"] = "cs", ["dabing"] = "cs",
        ["sk"] = "sk", ["slo"] = "sk", ["slovak"] = "sk",
        ["en"] = "en", ["eng"] = "en", ["english"] = "en",
        ["de"] = "de", ["ger"] = "de", ["hu"] = "hu", ["hun"] = "hu", ["pl"] = "pl", ["pol"] = "pl", ["fr"] = "fr", ["fre"] = "fr",
        ["es"] = "es", ["spa"] = "es", ["it"] = "it", ["ita"] = "it", ["ru"] = "ru", ["rus"] = "ru",
    };

    /// <param name="priecinky">Names of the folders from the disk's root down to the file's folder.</param>
    /// <param name="nazov">The file's name; for a Blu-ray or DVD structure the name of the folder that holds it.</param>
    /// <param name="strukturaDisku">The name is a folder holding BDMV or VIDEO_TS (one film).</param>
    /// <returns>Null when nothing can be read (an empty name).</returns>
    public static NavrhTitulu? Rozpoznaj(IReadOnlyList<string> priecinky, string nazov, bool strukturaDisku = false)
    {
        var kmen = strukturaDisku ? nazov : Path.GetFileNameWithoutExtension(nazov);
        var extra = JeExtra(kmen, priecinky);
        var epizoda = Epizoda(kmen);
        var seriaPriecinka = SeriaPriecinkov(priecinky);
        var knizne = priecinky.Select((p, i) => (p, i)).LastOrDefault(x =>
            x.p.Equals("MOVIES", StringComparison.OrdinalIgnoreCase) || x.p.Equals("TVSHOWS", StringComparison.OrdinalIgnoreCase));

        // UNIVERSUM's library: the folder under TVSHOWS or MOVIES is the title, exactly
        if (knizne.p is not null && knizne.i + 1 < priecinky.Count)
        {
            var titul = CitajNazov(priecinky[knizne.i + 1]);
            var popis = CitajNazov(kmen);
            if (titul.Nazov.Length > 0 && knizne.p.Equals("TVSHOWS", StringComparison.OrdinalIgnoreCase))
            {
                var epizody = epizoda?.Epizody ?? [];
                var seria = seriaPriecinka ?? epizoda?.Seria;
                return new NavrhTitulu(DruhTitulu.Serial, titul.Nazov, titul.Rok, titul.TmdbId, seria, epizody, null, popis.Jazyky,
                    popis.Kvalita, popis.Doplnok, IstotaNavrhu.Isty, extra || epizody.Count == 0);
            }

            if (titul.Nazov.Length > 0)
            {
                return new NavrhTitulu(DruhTitulu.Film, titul.Nazov, titul.Rok, titul.TmdbId, null, [], popis.Cast, popis.Jazyky, popis.Kvalita,
                    popis.Doplnok, IstotaNavrhu.Isty, extra);
            }
        }

        // an episode elsewhere: the series is named before "S01E04", or by the folder above "Season 01"
        if (epizoda is { } e && (e.Seria is not null || seriaPriecinka is not null))
        {
            var pred = CitajNazov(kmen[..e.Zaciatok]);
            var poEpizode = CitajNazov(kmen[e.Koniec..]);
            var meno = pred;
            if (meno.Nazov.Length == 0)
            {
                var index = seriaPriecinka is not null ? priecinky.Count - 2 : priecinky.Count - 1;
                meno = index >= 0 && !VseobecnePriecinky.Contains(priecinky[index]) ? CitajNazov(priecinky[index]) : meno;
            }

            if (meno.Nazov.Length == 0)
            {
                return null;
            }

            var istota = meno.TmdbId is not null ? IstotaNavrhu.Isty : meno.Rok is not null ? IstotaNavrhu.Pravdepodobny : IstotaNavrhu.Neisty;
            return new NavrhTitulu(DruhTitulu.Serial, meno.Nazov, meno.Rok, meno.TmdbId, e.Seria ?? seriaPriecinka, e.Epizody, null,
                pred.Jazyky.Concat(poEpizode.Jazyky).Distinct().ToList(), Max(pred.Kvalita, poEpizode.Kvalita), poEpizode.Doplnok, istota, extra);
        }

        // a film: the file's own name, or its folder's when that one says more (a year)
        var zoSuboru = CitajNazov(kmen);
        var priecinok = priecinky.Count > 0 && !VseobecnePriecinky.Contains(priecinky[^1]) && !PriecinkyExtra.Contains(priecinky[^1])
            ? CitajNazov(priecinky[^1])
            : default;
        var vybrany = zoSuboru.Nazov.Length > 0 && (zoSuboru.Rok is not null || priecinok.Rok is null || priecinok.Nazov.Length == 0)
            ? zoSuboru
            : priecinok.Nazov.Length > 0 ? priecinok with { Cast = zoSuboru.Cast, Jazyky = zoSuboru.Jazyky.Count > 0 ? zoSuboru.Jazyky : priecinok.Jazyky } : zoSuboru;
        if (vybrany.Nazov.Length == 0)
        {
            return null;
        }

        var istotaFilmu = (vybrany.TmdbId ?? priecinok.TmdbId) is not null ? IstotaNavrhu.Isty
            : vybrany.Rok is not null ? IstotaNavrhu.Pravdepodobny : IstotaNavrhu.Neisty;
        return new NavrhTitulu(DruhTitulu.Film, vybrany.Nazov, vybrany.Rok, vybrany.TmdbId ?? priecinok.TmdbId, null, [], vybrany.Cast,
            vybrany.Jazyky, Max(zoSuboru.Kvalita, priecinok.Kvalita), vybrany.Doplnok, istotaFilmu, extra);
    }

    /// <summary>What a name says, cleaned: title, year, TMDB id, part, languages, quality, version words.</summary>
    internal readonly record struct CitanyNazov(
        string Nazov, int? Rok, int? TmdbId, int? Cast, IReadOnlyList<string> Jazyky, TriedaKvality Kvalita, string? Doplnok)
    {
        public IReadOnlyList<string> Jazyky { get; init; } = Jazyky ?? [];
    }

    internal static CitanyNazov CitajNazov(string text)
    {
        int? tmdb = null;
        if (TmdbZnacka().Match(text) is { Success: true } znacka)
        {
            tmdb = int.Parse(znacka.Groups["id"].Value, CultureInfo.InvariantCulture);
            text = text.Remove(znacka.Index, znacka.Length);
        }

        text = InaZnacka().Replace(text, " ");
        int? cast = null;
        if (CastFilmu().Match(text) is { Success: true } c)
        {
            cast = int.Parse(c.Groups["cislo"].Value, CultureInfo.InvariantCulture);
            text = text.Remove(c.Index, c.Length);
        }

        // UNIVERSUM: "Názov (rok) - CZ EN FHD H264 Directors Cut SMALL"
        string? popis = null;
        if (KnizneMeno().Match(text) is { Success: true } knizne)
        {
            popis = knizne.Groups["popis"].Value;
            text = knizne.Groups["titul"].Value;
        }

        // release names write dots or underscores for spaces ("Mr. Robot" keeps its dot, it has spaces)
        if (!text.Contains(' ', StringComparison.Ordinal) || text.Count(z => z == '.') > 2)
        {
            text = text.Replace('_', ' ').Replace('.', ' ');
        }

        text = SkupinaNaZaciatku().Replace(text, "");

        // a year in brackets ends the title, whatever the title says ("Názov (2012) 1080p", "Vymyslený Blu-ray (2012)")
        if (RokVZatvorke().Matches(text) is { Count: > 0 } roky && roky[^1] is var zatvorka && zatvorka.Index > 0)
        {
            var predRokom = text[..zatvorka.Index].Trim(' ', '-', '–', ',');
            if (predRokom.Length > 0)
            {
                var poRoku = text[(zatvorka.Index + zatvorka.Length)..].Split(' ', StringSplitOptions.RemoveEmptyEntries)
                    .Concat(popis?.Split(' ', StringSplitOptions.RemoveEmptyEntries) ?? []).ToList();
                var (j, k, d) = Popis(poRoku, popis is not null);
                return new CitanyNazov(predRokom, int.Parse(zatvorka.Groups["r"].Value, CultureInfo.InvariantCulture), tmdb, cast, j, k, d);
            }
        }

        var slova = text.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();

        // the title ends at the first release word; its year is the last year before it (not the first word:
        // "2001 A Space Odyssey", "Blade Runner 2049 2017")
        var koniec = slova.FindIndex(s => Zastavky.Contains(s.Trim('(', ')', '[', ']', '-')));
        if (koniec < 0)
        {
            koniec = slova.Count;
        }

        int? rok = null;
        var koniecNazvu = koniec;
        for (var i = koniec - 1; i >= 1; i--)
        {
            if (Rok(slova[i]) is { } r)
            {
                rok = r;
                koniecNazvu = i;
                break;
            }
        }

        var nazov = string.Join(' ', slova.Take(koniecNazvu)).Trim(' ', '-', '–', ',', '(', '[');
        var zvysok = slova.Skip(koniecNazvu + (rok is null ? 0 : 1)).Concat(popis?.Split(' ', StringSplitOptions.RemoveEmptyEntries) ?? []).ToList();
        var (jazyky, kvalita, doplnok) = Popis(zvysok, popis is not null);
        return new CitanyNazov(nazov, rok, tmdb, cast, jazyky, kvalita, doplnok);
    }

    /// <summary>Languages of sound, quality and version words from the words after the title.</summary>
    private static (IReadOnlyList<string> Jazyky, TriedaKvality Kvalita, string? Doplnok) Popis(IReadOnlyList<string> slova, bool knizne)
    {
        var jazyky = new List<string>();
        var kvalita = TriedaKvality.Nezistena;
        var doplnok = new List<string>();
        for (var i = 0; i < slova.Count; i++)
        {
            var slovo = slova[i].Trim('(', ')', '[', ']', '-', ',');
            // "CZ titulky": Czech subtitles, not Czech sound
            var titulky = i + 1 < slova.Count && slova[i + 1].Equals("titulky", StringComparison.OrdinalIgnoreCase);
            if (JazykyNazvov.TryGetValue(slovo, out var jazyk) && (slovo.Length <= 3 || !knizne) && !titulky)
            {
                if (!(slovo.Equals("dabing", StringComparison.OrdinalIgnoreCase) && jazyky.Count > 0) && !jazyky.Contains(jazyk))
                {
                    jazyky.Add(jazyk);
                }

                continue;
            }

            var trieda = slovo.ToLowerInvariant() switch
            {
                "2160p" or "4k" or "uhd" => TriedaKvality.UHD4K,
                "1080p" or "1080i" or "fhd" => TriedaKvality.FHD,
                "720p" or "hd" => TriedaKvality.HD,
                "576p" or "480p" or "sd" => TriedaKvality.SD,
                _ => TriedaKvality.Nezistena,
            };
            if (trieda != TriedaKvality.Nezistena)
            {
                kvalita = Max(kvalita, trieda);
                continue;
            }

            // in UNIVERSUM's names the words that are not language, quality, codec or size class tell versions apart
            if (knizne && !Zastavky.Contains(slovo) && !slovo.Equals("BluRay", StringComparison.OrdinalIgnoreCase) && slovo.Length > 0 &&
                !char.IsDigit(slovo[^1]) || knizne && slovo.Length > 0 && char.IsDigit(slovo[0]) && !Zastavky.Contains(slovo) && !Rok(slovo).HasValue)
            {
                if (!slovo.Equals("titulky", StringComparison.OrdinalIgnoreCase) && !Kodek(slovo))
                {
                    doplnok.Add(slova[i].Trim());
                }
            }
        }

        return (jazyky, kvalita, doplnok.Count == 0 ? null : string.Join(' ', doplnok));
    }

    private static bool Kodek(string slovo) => slovo.ToLowerInvariant() is "h264" or "h265" or "hevc" or "av1" or "vp9" or "mpeg2" or "mpeg4" or "xvid";

    /// <summary>The episode in a name: "S01E04", "S01E04E05", "S01E04-E06", "1x04", "Díl 4", "Episode 4", "E04".</summary>
    internal static (int? Seria, IReadOnlyList<int> Epizody, int Zaciatok, int Koniec)? Epizoda(string kmen)
    {
        if (SxxEyy().Match(kmen) is { Success: true } m)
        {
            var seria = int.Parse(m.Groups["s"].Value, CultureInfo.InvariantCulture);
            var prva = int.Parse(m.Groups["e"].Value, CultureInfo.InvariantCulture);
            var epizody = new List<int> { prva };
            foreach (Capture dalsia in m.Groups["dalsie"].Captures)
            {
                var cislo = int.Parse(dalsia.Value, CultureInfo.InvariantCulture);
                if (cislo > epizody[^1] && cislo - epizody[^1] <= 10)
                {
                    // "E04-E06" is 4, 5 and 6
                    for (var k = epizody[^1] + 1; k <= cislo; k++)
                    {
                        epizody.Add(k);
                    }
                }
            }

            return (seria, epizody, m.Index, m.Index + m.Length);
        }

        if (NxM().Match(kmen) is { Success: true } x)
        {
            return (int.Parse(x.Groups["s"].Value, CultureInfo.InvariantCulture), [int.Parse(x.Groups["e"].Value, CultureInfo.InvariantCulture)],
                x.Index, x.Index + x.Length);
        }

        if (SlovoEpizody().Match(kmen) is { Success: true } s)
        {
            return (null, [int.Parse(s.Groups["e"].Value, CultureInfo.InvariantCulture)], s.Index, s.Index + s.Length);
        }

        return null;
    }

    /// <summary>The season of the nearest season folder ("Season 01", "Séria 2", "S03", "Specials" = 0), at most two levels up.</summary>
    internal static int? SeriaPriecinkov(IReadOnlyList<string> priecinky)
    {
        for (var i = priecinky.Count - 1; i >= Math.Max(0, priecinky.Count - 2); i--)
        {
            if (Specialy().IsMatch(priecinky[i]))
            {
                return 0;
            }

            if (PriecinokSerie().Match(priecinky[i]) is { Success: true } m)
            {
                return int.Parse(m.Groups["s"].Value, CultureInfo.InvariantCulture);
            }
        }

        return null;
    }

    private static bool JeExtra(string kmen, IReadOnlyList<string> priecinky) =>
        ExtraVNazve().IsMatch(kmen) || priecinky.Skip(Math.Max(0, priecinky.Count - 2)).Any(PriecinkyExtra.Contains);

    private static int? Rok(string slovo)
    {
        var s = slovo.Trim('(', ')', '[', ']', ',', '-');
        return s.Length == 4 && int.TryParse(s, NumberStyles.None, CultureInfo.InvariantCulture, out var r) && r is >= 1890 and <= 2099 ? r : null;
    }

    private static TriedaKvality Max(TriedaKvality a, TriedaKvality b) => (TriedaKvality)Math.Max((int)a, (int)b);

    [GeneratedRegex(@"[\[\{]\s*tmdb(?:id)?\s*[-=:]\s*(?<id>\d{1,9})\s*[\]\}]", RegexOptions.IgnoreCase)]
    private static partial Regex TmdbZnacka();

    [GeneratedRegex(@"[\[\{]\s*(?:imdb(?:id)?|tvdb(?:id)?)\s*[-=:]\s*[a-z0-9]+\s*[\]\}]", RegexOptions.IgnoreCase)]
    private static partial Regex InaZnacka();

    // only at the end of the name: "Názov Part 2 (2004)" is a title, "Názov (2004) CD2" is a part
    [GeneratedRegex(@"[\s._\-\[(]+(?:cd|part|pt|disc|disk|dvd)[\s._-]?(?<cislo>[1-9])[\])]?\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex CastFilmu();

    [GeneratedRegex(@"^(?<titul>.+?\(\d{4}\))\s+-\s+(?<popis>.+)$")]
    private static partial Regex KnizneMeno();

    [GeneratedRegex(@"[\(\[](?<r>(?:18|19|20)\d{2})[\)\]]")]
    private static partial Regex RokVZatvorke();

    [GeneratedRegex(@"^\s*\[[^\]]*\]\s*")]
    private static partial Regex SkupinaNaZaciatku();

    [GeneratedRegex(@"(?<![a-z0-9])s(?<s>\d{1,2})[\s._-]*e(?<e>\d{1,3})(?:[\s._-]*-?[\s._-]*e?(?<dalsie>\d{1,3})(?![0-9p]))*", RegexOptions.IgnoreCase)]
    private static partial Regex SxxEyy();

    [GeneratedRegex(@"(?<![a-z0-9])(?<s>\d{1,2})x(?<e>\d{2,3})(?![0-9])", RegexOptions.IgnoreCase)]
    private static partial Regex NxM();

    [GeneratedRegex(@"(?<![a-z])(?:episode|epizoda|epizóda|díl|diel|dil|časť|cast|folge|ep|e)[\s._-]*(?<e>\d{1,3})(?![0-9p])", RegexOptions.IgnoreCase)]
    private static partial Regex SlovoEpizody();

    [GeneratedRegex(@"^(?:season|séria|seria|série|serie|sezóna|sezona|staffel|s)[\s._-]*(?<s>\d{1,2})$", RegexOptions.IgnoreCase)]
    private static partial Regex PriecinokSerie();

    [GeneratedRegex(@"(?:^|\s)(?:specials?|špeciály|specialy|špeciálne epizódy|extras season)$|^season 0+$", RegexOptions.IgnoreCase)]
    private static partial Regex Specialy();

    [GeneratedRegex(@"(?:^|[\s._-])(?:sample|trailer|teaser|ukážka|ukazka)(?:$|[\s._-])", RegexOptions.IgnoreCase)]
    private static partial Regex ExtraVNazve();
}
