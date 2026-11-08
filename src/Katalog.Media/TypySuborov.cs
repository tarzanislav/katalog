namespace Katalog.Media;

/// <summary>Which files are videos, subtitles or disc structures (by extension, lower case without the dot).</summary>
public static class TypySuborov
{
    private static readonly HashSet<string> Video = new(StringComparer.OrdinalIgnoreCase)
    {
        "mkv", "mp4", "m4v", "avi", "ts", "m2ts", "mts", "mov", "wmv", "mpg", "mpeg", "vob", "webm", "flv", "ogv", "divx", "3gp", "iso",
    };

    private static readonly HashSet<string> Titulky = new(StringComparer.OrdinalIgnoreCase)
    {
        "srt", "ass", "ssa", "sub", "idx", "sup", "vtt",
    };

    public static bool JeVideo(string pripona) => Video.Contains(pripona);

    public static bool SuTitulky(string pripona) => Titulky.Contains(pripona);

    /// <summary>A DVD (VIDEO_TS) or Blu-ray (BDMV) folder: the whole structure is one title, not a heap of files.</summary>
    public static bool JeStrukturaDisku(string nazovPriecinka) =>
        nazovPriecinka.Equals("VIDEO_TS", StringComparison.OrdinalIgnoreCase) ||
        nazovPriecinka.Equals("BDMV", StringComparison.OrdinalIgnoreCase);

    /// <summary>Extension of a file name, lower case without the dot ("" when there is none).</summary>
    public static string Pripona(string nazov)
    {
        var bodka = nazov.LastIndexOf('.');
        return bodka <= 0 || bodka == nazov.Length - 1 ? "" : nazov[(bodka + 1)..].ToLowerInvariant();
    }
}
