namespace Katalog.Jadro.Model;

/// <summary>Kinds of files by extension, as the query language (typ:video) and the window name them.</summary>
public enum DruhSuboru
{
    Video = 1,
    Titulky = 2,
    Zvuk = 3,
    Obrazok = 4,
    Dokument = 5,
    Archiv = 6,
}

/// <summary>Which extensions (lower case, without the dot) belong to which kind of file.</summary>
public static class DruhySuborov
{
    private static readonly Dictionary<DruhSuboru, string[]> Zoznam = new()
    {
        [DruhSuboru.Video] =
        [
            "mkv", "mp4", "m4v", "avi", "ts", "m2ts", "mts", "mov", "wmv", "mpg", "mpeg", "vob", "webm", "flv", "ogv", "divx", "3gp", "iso",
        ],
        [DruhSuboru.Titulky] = ["srt", "ass", "ssa", "sub", "idx", "sup", "vtt"],
        [DruhSuboru.Zvuk] = ["mp3", "flac", "aac", "ac3", "eac3", "dts", "thd", "m4a", "ogg", "opus", "wav", "wma", "mka"],
        [DruhSuboru.Obrazok] = ["jpg", "jpeg", "png", "gif", "bmp", "webp", "tif", "tiff", "heic"],
        [DruhSuboru.Dokument] = ["txt", "nfo", "pdf", "doc", "docx", "odt", "rtf", "md"],
        [DruhSuboru.Archiv] = ["zip", "rar", "7z", "tar", "gz", "bz2", "xz"],
    };

    private static readonly Dictionary<string, DruhSuboru> PodlaPripony = Zoznam
        .SelectMany(d => d.Value.Select(p => (Pripona: p, Druh: d.Key)))
        .ToDictionary(x => x.Pripona, x => x.Druh, StringComparer.OrdinalIgnoreCase);

    public static IReadOnlyList<string> Pripony(DruhSuboru druh) => Zoznam[druh];

    /// <summary>The kind of a file with this extension, or null when it is none of the known kinds.</summary>
    public static DruhSuboru? Urci(string pripona) => PodlaPripony.TryGetValue(pripona, out var druh) ? druh : null;

    public static bool JeVideo(string pripona) => Urci(pripona) == DruhSuboru.Video;
}
