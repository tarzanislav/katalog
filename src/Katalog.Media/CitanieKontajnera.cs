namespace Katalog.Media;

/// <summary>
/// KATALOG's own reading of technical data, before ffprobe: Matroska/WebM and MP4/QuickTime by their first bytes. It
/// reads only the head of the file and its index (kilobytes, not the film), so a disk of films is read in seconds and
/// FFmpeg is not needed for them. Null = not such a file, or the reader cannot follow it (ffprobe then).
/// </summary>
public static class CitanieKontajnera
{
    public static VysledokKontajnera? Citaj(string cesta)
    {
        Span<byte> zaciatok = stackalloc byte[12];
        try
        {
            using (var subor = new FileStream(cesta, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 1, FileOptions.None))
            {
                if (subor.ReadAtLeast(zaciatok, zaciatok.Length, throwOnEndOfStream: false) < zaciatok.Length)
                {
                    return null;
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }

        var info = zaciatok[..4].SequenceEqual((ReadOnlySpan<byte>)[0x1A, 0x45, 0xDF, 0xA3]) ? CitanieMatroska.Citaj(cesta)
            : zaciatok.Slice(4, 4).SequenceEqual("ftyp"u8) || zaciatok.Slice(4, 4).SequenceEqual("moov"u8) ? CitanieMp4.Citaj(cesta)
            : null;
        return info is null ? null : PopisKontajnera.Vytvor(info);
    }
}
