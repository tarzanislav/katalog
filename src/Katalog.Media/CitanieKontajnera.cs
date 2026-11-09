namespace Katalog.Media;

/// <summary>
/// KATALOG's one reader of video files, before any outside program: it tells the format by the first bytes – Matroska
/// and WebM, MP4 and QuickTime, AVI, MPEG transport streams (TS, M2TS, MTS) and program streams (VOB, MPG) – and reads
/// only the head of the file, its index and a few windows (kilobytes to a few megabytes, not the film), so a disk of
/// films of any of these formats is read in one go without starting a program for each file. Null = a format it does
/// not know (WMV, FLV, RealMedia …) or a file it cannot follow: ffprobe reads it then.
/// </summary>
public static class CitanieKontajnera
{
    /// <summary>The extensions the reader knows: their details are read without ffprobe (and read again once for the details).</summary>
    public static readonly IReadOnlySet<string> Pripony = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "mkv", "mk3d", "webm", "mp4", "m4v", "mov", "avi", "divx", "ts", "m2ts", "mts", "m2t", "trp", "tp", "vob", "mpg", "mpeg", "m2v", "m2p",
    };

    public static VysledokKontajnera? Citaj(string cesta)
    {
        Span<byte> zaciatok = stackalloc byte[1024];
        int precitane;
        try
        {
            using var subor = new FileStream(cesta, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 1, FileOptions.None);
            precitane = subor.ReadAtLeast(zaciatok, zaciatok.Length, throwOnEndOfStream: false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }

        var info = Format(zaciatok[..precitane]) switch
        {
            FormatSuboru.Matroska => CitanieMatroska.Citaj(cesta),
            FormatSuboru.Mp4 => CitanieMp4.Citaj(cesta),
            FormatSuboru.Avi => CitanieAvi.Citaj(cesta),
            FormatSuboru.TransportnyTok => CitanieTs.Citaj(cesta),
            FormatSuboru.ProgramovyTok => CitaniePs.Citaj(cesta),
            _ => null,
        };
        return info is null ? null : PopisKontajnera.Vytvor(info);
    }

    internal enum FormatSuboru
    {
        Neznamy,
        Matroska,
        Mp4,
        Avi,
        TransportnyTok,
        ProgramovyTok,
    }

    /// <summary>The format by the first bytes of a file (its name does not matter).</summary>
    internal static FormatSuboru Format(ReadOnlySpan<byte> z)
    {
        if (z.Length < 12)
        {
            return FormatSuboru.Neznamy;
        }

        if (z[..4].SequenceEqual((ReadOnlySpan<byte>)[0x1A, 0x45, 0xDF, 0xA3]))
        {
            return FormatSuboru.Matroska;
        }

        var box = z.Slice(4, 4);
        if (box.SequenceEqual("ftyp"u8) || box.SequenceEqual("moov"u8) || box.SequenceEqual("mdat"u8) || box.SequenceEqual("free"u8) ||
            box.SequenceEqual("wide"u8) || box.SequenceEqual("skip"u8))
        {
            return FormatSuboru.Mp4;
        }

        if (z[..4].SequenceEqual("RIFF"u8) && z.Slice(8, 4).SequenceEqual("AVI "u8))
        {
            return FormatSuboru.Avi;
        }

        if (z[0] == 0 && z[1] == 0 && z[2] == 1 && z[3] == 0xBA)
        {
            return FormatSuboru.ProgramovyTok;
        }

        return CitanieTs.Paket(z) is not null ? FormatSuboru.TransportnyTok : FormatSuboru.Neznamy;
    }
}
