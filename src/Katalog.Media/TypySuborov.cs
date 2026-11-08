using Katalog.Jadro.Model;

namespace Katalog.Media;

/// <summary>Which files are videos, subtitles or disc structures (by extension, lower case without the dot).</summary>
public static class TypySuborov
{
    public static bool JeVideo(string pripona) => DruhySuborov.Urci(pripona) == DruhSuboru.Video;

    public static bool SuTitulky(string pripona) => DruhySuborov.Urci(pripona) == DruhSuboru.Titulky;

    /// <summary>A DVD (VIDEO_TS) or Blu-ray (BDMV) folder: the whole structure is one title, not a heap of files.</summary>
    public static bool JeStrukturaDisku(string nazovPriecinka) =>
        nazovPriecinka.Equals("VIDEO_TS", StringComparison.OrdinalIgnoreCase) ||
        nazovPriecinka.Equals("BDMV", StringComparison.OrdinalIgnoreCase);

    /// <summary>Extension of a file name, lower case without the dot ("" when there is none).</summary>
    public static string Pripona(string nazov) => NazovSuboru.Pripona(nazov);
}
