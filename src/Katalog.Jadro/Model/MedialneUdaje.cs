using System.Globalization;

namespace Katalog.Jadro.Model;

/// <summary>Technical data of a video file as ffprobe reads it (stage E3).</summary>
/// <param name="DatovyTok">Bits per second of the whole file.</param>
/// <param name="Snimky">Frames per second of the video.</param>
/// <param name="Chyby">What ffprobe reported as wrong (a damaged file goes to review, N3); null when nothing.</param>
public sealed record MedialneUdaje(
    string? Kontajner,
    long? DlzkaMs,
    long? DatovyTok,
    string? VideoKodek,
    int? Sirka,
    int? Vyska,
    Hdr Hdr,
    double? Snimky,
    string? Chyby)
{
    public TriedaKvality Trieda => Sirka is { } s && Vyska is { } v ? Kvalita.Z(s, v) : TriedaKvality.Nezistena;

    /// <summary>A file ffprobe could not read at all.</summary>
    public static MedialneUdaje Necitatelne(string chyba) => new(null, null, null, null, null, null, Hdr.Ziadne, null, chyba);
}

/// <summary>An audio or subtitle track (or the video track) of a file, or an external subtitle file next to it.</summary>
/// <param name="Jazyk">ISO 639-1 where known ("cs"), otherwise as the file says; null when not stated.</param>
/// <param name="Kodek">ffprobe's codec name, refined by its profile ("dts" with "DTS-HD MA" → "dts-hd ma").</param>
/// <param name="Sdh">Subtitles for the deaf and hard of hearing.</param>
/// <param name="ExternySubor">For an external subtitle file: its id in the library.</param>
public sealed record Stopa(
    int Poradie,
    DruhStopy Druh,
    string? Jazyk,
    string? Kodek,
    int? Kanaly,
    bool Predvolena,
    bool Vynutena,
    bool Sdh,
    string? Nazov,
    long? ExternySubor = null);

/// <summary>How technical data are written in the window ("H.265", "DTS-HD MA 5.1", "2:03:15").</summary>
public static class PopisMedii
{
    public static string Kodek(string? kodek) => kodek?.ToLowerInvariant() switch
    {
        null or "" => "",
        "vvc" => "H.266",
        "hevc" => "H.265",
        "prores" => "ProRes",
        "theora" => "Theora",
        "ffv1" => "FFV1",
        "h264" => "H.264",
        "av1" => "AV1",
        "vp9" => "VP9",
        "vp8" => "VP8",
        "mpeg2video" => "MPEG-2",
        "mpeg4" => "MPEG-4",
        "mpeg1video" => "MPEG-1",
        "vc1" => "VC-1",
        "msmpeg4v3" => "DivX 3",
        "mjpeg" => "MJPEG",
        "aac" => "AAC",
        "ac3" => "AC-3",
        "eac3" => "E-AC-3",
        "truehd" => "TrueHD",
        "truehd atmos" => "TrueHD Atmos",
        "eac3 atmos" => "E-AC-3 Atmos",
        "ac4" => "AC-4",
        "mpegh" => "MPEG-H 3D",
        "alac" => "ALAC",
        "wmapro" or "wmav2" => "WMA",
        "dts" => "DTS",
        "dts-hd ma" => "DTS-HD MA",
        "dts-hd hra" => "DTS-HD HRA",
        "dts:x" => "DTS:X",
        "flac" => "FLAC",
        "mp3" => "MP3",
        "mp2" => "MP2",
        "opus" => "Opus",
        "vorbis" => "Vorbis",
        "subrip" or "srt" => "SRT",
        "ass" or "ssa" => "ASS",
        "hdmv_pgs_subtitle" or "sup" => "PGS",
        "dvd_subtitle" or "vobsub" => "VobSub",
        "mov_text" => "TX3G",
        "webvtt" or "vtt" => "WebVTT",
        var k when k.StartsWith("pcm_", StringComparison.Ordinal) => "PCM",
        var k => k.ToUpperInvariant(),
    };

    /// <summary>"5.1" for 6 channels, "stereo", "mono", otherwise the count.</summary>
    public static string Kanaly(int? kanaly) => kanaly switch
    {
        null => "",
        1 => "mono",
        2 => "stereo",
        6 => "5.1",
        8 => "7.1",
        var k => k.Value.ToString(CultureInfo.InvariantCulture) + " kanálov",
    };

    public static string Hdr(Hdr hdr) => hdr switch
    {
        Model.Hdr.Hdr10 => "HDR10",
        Model.Hdr.Hdr10Plus => "HDR10+",
        Model.Hdr.DolbyVision => "Dolby Vision",
        Model.Hdr.Hlg => "HLG",
        _ => "",
    };

    /// <summary>"1:45:03" or "42:10".</summary>
    public static string Dlzka(long ms)
    {
        var cas = TimeSpan.FromMilliseconds(ms);
        return cas.TotalHours >= 1
            ? string.Create(CultureInfo.InvariantCulture, $"{(int)cas.TotalHours}:{cas.Minutes:00}:{cas.Seconds:00}")
            : string.Create(CultureInfo.InvariantCulture, $"{cas.Minutes}:{cas.Seconds:00}");
    }
}
