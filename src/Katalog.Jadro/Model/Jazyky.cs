namespace Katalog.Jadro.Model;

/// <summary>
/// Languages of tracks: files name them in ISO 639-2 (bibliographic "cze" or terminological "ces"), sometimes ISO 639-1
/// or by name. KATALOG keeps ISO 639-1 where it knows one, so "zvuk:cs" finds "cze", "ces" and "cs" alike.
/// </summary>
public static class Jazyky
{
    private static readonly (string Kod, string[] Ine, string Nazov)[] Zname =
    [
        ("sk", ["slo", "slk", "slovak", "slovencina"], "slovenčina"),
        ("cs", ["cze", "ces", "cz", "czech", "cestina"], "čeština"),
        ("en", ["eng", "english", "anglictina"], "angličtina"),
        ("de", ["ger", "deu", "german", "nemcina"], "nemčina"),
        ("hu", ["hun", "hungarian", "madarcina"], "maďarčina"),
        ("pl", ["pol", "polish", "polstina"], "poľština"),
        ("fr", ["fre", "fra", "french", "francuzstina"], "francúzština"),
        ("es", ["spa", "spanish", "spanielcina"], "španielčina"),
        ("it", ["ita", "italian", "taliancina"], "taliančina"),
        ("pt", ["por", "portuguese"], "portugalčina"),
        ("ru", ["rus", "russian", "rustina"], "ruština"),
        ("uk", ["ukr", "ukrainian"], "ukrajinčina"),
        ("hr", ["hrv", "scr", "croatian"], "chorvátčina"),
        ("sr", ["srp", "scc", "serbian"], "srbčina"),
        ("sl", ["slv", "slovenian"], "slovinčina"),
        ("ro", ["rum", "ron", "romanian"], "rumunčina"),
        ("bg", ["bul", "bulgarian"], "bulharčina"),
        ("nl", ["dut", "nld", "dutch"], "holandčina"),
        ("sv", ["swe", "swedish"], "švédčina"),
        ("da", ["dan", "danish"], "dánčina"),
        ("no", ["nor", "nob", "nno", "norwegian"], "nórčina"),
        ("fi", ["fin", "finnish"], "fínčina"),
        ("el", ["gre", "ell", "greek"], "gréčtina"),
        ("tr", ["tur", "turkish"], "turečtina"),
        ("he", ["heb", "hebrew"], "hebrejčina"),
        ("ar", ["ara", "arabic"], "arabčina"),
        ("hi", ["hin", "hindi"], "hindčina"),
        ("ja", ["jpn", "japanese"], "japončina"),
        ("ko", ["kor", "korean"], "kórejčina"),
        ("zh", ["chi", "zho", "chinese"], "čínština"),
        ("th", ["tha", "thai"], "thajčina"),
        ("vi", ["vie", "vietnamese"], "vietnamčina"),
        ("la", ["lat", "latin"], "latinčina"),
    ];

    private static readonly Dictionary<string, string> Kody = Zname
        .SelectMany(z => z.Ine.Append(z.Kod).Select(i => (Ine: i, z.Kod)))
        .ToDictionary(x => x.Ine, x => x.Kod, StringComparer.OrdinalIgnoreCase);

    private static readonly Dictionary<string, string> Nazvy = Zname.ToDictionary(z => z.Kod, z => z.Nazov);

    /// <summary>
    /// The language as KATALOG keeps it: ISO 639-1 when known, otherwise the code in lower case; null for "und", "zxx",
    /// "mul" and empty.
    /// </summary>
    public static string? Normalizuj(string? jazyk)
    {
        var j = jazyk?.Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(j) || j is "und" or "zxx" or "mis" or "mul" or "unknown" or "nezistený")
        {
            return null;
        }

        return Kody.TryGetValue(j, out var kod) ? kod : j;
    }

    /// <summary>"slovenčina" for "sk"; the code itself when the language is not among the known ones.</summary>
    public static string Nazov(string kod) => Nazvy.TryGetValue(kod, out var nazov) ? nazov : kod;

    /// <summary>
    /// The language of an external subtitle file from the end of its name: "Film (2024).sk.srt" → "sk",
    /// "Film.cze.forced.srt" → "cs" with forced; null when the name says none.
    /// </summary>
    public static (string? Jazyk, bool Vynutene, bool Sdh) ZNazvuTitulkov(string nazovSuboru, string nazovVidea)
    {
        var bezPripony = nazovSuboru[..Math.Max(0, nazovSuboru.LastIndexOf('.'))];
        var video = nazovVidea[..Math.Max(0, nazovVidea.LastIndexOf('.'))];
        // only what follows the video's name (or, for another name, its dotted endings): "It (2017).srt" is no Italian
        var zvysok = bezPripony.Length > video.Length && bezPripony.StartsWith(video, StringComparison.OrdinalIgnoreCase)
            ? bezPripony[video.Length..]
            : bezPripony.IndexOf('.', StringComparison.Ordinal) is var bodka and >= 0 ? bezPripony[bodka..] : "";
        string? jazyk = null;
        bool vynutene = false, sdh = false;
        foreach (var cast in zvysok.Split(['.', '_', '-', ' '], StringSplitOptions.RemoveEmptyEntries))
        {
            var c = cast.ToLowerInvariant();
            if (c is "forced" or "vynutene" or "vynútené")
            {
                vynutene = true;
            }
            else if (c is "sdh" or "hi" or "cc")
            {
                sdh = true;
            }
            else if (Kody.TryGetValue(c, out var kod))
            {
                jazyk = kod;
            }
        }

        return (jazyk, vynutene, sdh);
    }
}
