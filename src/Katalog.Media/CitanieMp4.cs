using System.Buffers.Binary;
using System.Globalization;
using System.Text;
using Katalog.Jadro.Model;

namespace Katalog.Media;

/// <summary>
/// KATALOG's own reader of MP4 and QuickTime files (ISO base media): only the top-level boxes are walked and the movie
/// box (moov) is read – wherever it lies – with the description of each track: codec and its configuration, picture
/// size and colours, sound, language, and from the sample tables the size, count of frames and frame rate. A file it
/// cannot follow gives null (ffprobe reads it then).
/// </summary>
public static class CitanieMp4
{
    /// <summary>A movie box bigger than this is not read (a broken size).</summary>
    private const long NajviacMoov = 64L << 20;

    public static InfoSuboru? Citaj(string cesta)
    {
        try
        {
            using var subor = new FileStream(cesta, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 4096, FileOptions.RandomAccess);
            return Citaj(subor);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or EndOfStreamException or ArgumentException or InvalidDataException)
        {
            return null;
        }
    }

    /// <summary>Never throws for what a file contains: a damaged or cut file gives null.</summary>
    public static InfoSuboru? Citaj(Stream subor)
    {
        try
        {
            return CitajBezpecne(subor);
        }
        catch (Exception ex) when (ex is IOException or EndOfStreamException or ArgumentException or InvalidDataException or OverflowException or IndexOutOfRangeException)
        {
            return null;
        }
    }

    private static InfoSuboru? CitajBezpecne(Stream subor)
    {
        var info = new InfoSuboru { Velkost = subor.Length, Format = "MPEG-4" };
        byte[]? moov = null;
        long pos = 0;
        var prvy = true;
        Span<byte> hlava = stackalloc byte[16];
        while (pos + 8 <= subor.Length)
        {
            subor.Position = pos;
            if (subor.ReadAtLeast(hlava, 16, throwOnEndOfStream: false) < 8)
            {
                break;
            }

            long velkost = BinaryPrimitives.ReadUInt32BigEndian(hlava);
            var typ = Encoding.ASCII.GetString(hlava.Slice(4, 4));
            var hlavicka = 8;
            if (velkost == 1)
            {
                velkost = (long)BinaryPrimitives.ReadUInt64BigEndian(hlava[8..]);
                hlavicka = 16;
            }
            else if (velkost == 0)
            {
                velkost = subor.Length - pos;
            }

            if (prvy && typ is not ("ftyp" or "moov" or "mdat" or "free" or "skip" or "wide" or "pnot"))
            {
                return null;
            }

            prvy = false;
            if (velkost < hlavicka)
            {
                break;
            }

            if (typ == "ftyp" && velkost >= 12)
            {
                var z = hlavicka == 8 ? Encoding.ASCII.GetString(hlava.Slice(8, 4)) : "";
                info.Format = z == "qt  " ? "QuickTime" : "MPEG-4";
                info.VerziaFormatu = z.Trim() switch { "isom" or "iso2" or "mp41" or "mp42" or "avc1" => "Base Media / " + z.Trim(), "M4V" => "Apple M4V", var x => x };
            }
            else if (typ == "moov" && velkost - hlavicka <= NajviacMoov)
            {
                moov = new byte[velkost - hlavicka];
                subor.Position = pos + hlavicka;
                subor.ReadExactly(moov);
            }

            pos += velkost;
        }

        if (moov is null)
        {
            return null;
        }

        foreach (var (typ, data) in Boxy(moov))
        {
            switch (typ)
            {
                case "mvhd":
                    Mvhd(data, info);
                    break;
                case "trak":
                    if (Trak(data) is { } stopa)
                    {
                        info.Stopy.Add(stopa);
                    }

                    break;
                case "udta":
                    Udta(data, info);
                    break;
            }
        }

        return info.Stopy.Count == 0 ? null : info;
    }

    private static void Mvhd(byte[] d, InfoSuboru info)
    {
        if (d.Length < 32)
        {
            return;
        }

        var (mierka, trvanie, vytvorene) = d[0] == 1
            ? (BinaryPrimitives.ReadUInt32BigEndian(d.AsSpan(20)), (double)BinaryPrimitives.ReadUInt64BigEndian(d.AsSpan(24)), (long)BinaryPrimitives.ReadUInt64BigEndian(d.AsSpan(4)))
            : (BinaryPrimitives.ReadUInt32BigEndian(d.AsSpan(12)), BinaryPrimitives.ReadUInt32BigEndian(d.AsSpan(16)), (long)BinaryPrimitives.ReadUInt32BigEndian(d.AsSpan(4)));
        if (mierka > 0 && trvanie > 0)
        {
            info.DlzkaSekund = trvanie / mierka;
        }

        if (vytvorene > 0)
        {
            info.DatumKodovania = new DateTime(1904, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddSeconds(vytvorene);
        }
    }

    private static void Udta(byte[] udta, InfoSuboru info)
    {
        foreach (var (typ, data) in Boxy(udta))
        {
            if (typ == "meta" && data.Length > 4)
            {
                foreach (var (mtyp, mdata) in Boxy(data[4..]))
                {
                    if (mtyp != "ilst")
                    {
                        continue;
                    }

                    foreach (var (ityp, idata) in Boxy(mdata))
                    {
                        var text = Boxy(idata).Where(b => b.Typ == "data" && b.Data.Length > 8).Select(b => Encoding.UTF8.GetString(b.Data, 8, b.Data.Length - 8)).FirstOrDefault();
                        if (ityp == "©too")
                        {
                            info.Aplikacia = text;
                        }
                        else if (ityp == "©nam")
                        {
                            info.Nazov = text;
                        }
                    }
                }
            }
        }
    }

    private static InfoStopy? Trak(byte[] trak)
    {
        var s = new InfoStopy();
        string? handler = null;
        var zapnuta = true;
        foreach (var (typ, data) in Boxy(trak))
        {
            if (typ == "tkhd" && data.Length >= 84)
            {
                zapnuta = (data[3] & 1) == 1;
                s.Id = (int)BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(data[0] == 1 ? 20 : 12));
                var w = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(data.Length - 8)) / 65536.0;
                var h = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(data.Length - 4)) / 65536.0;
                if (w > 0 && h > 0)
                {
                    s.PomerStran = w / h;
                }
            }
            else if (typ == "mdia")
            {
                handler = Mdia(data, s);
            }
        }

        s.Druh = handler switch
        {
            "vide" => DruhStopy.Video,
            "soun" => DruhStopy.Zvuk,
            "subt" or "text" or "sbtl" or "clcp" => DruhStopy.Titulky,
            _ => null,
        };
        s.JeVideo = s.Druh == DruhStopy.Video && s.Kodek is not ("mjpeg" or "png");
        s.Predvolena = zapnuta;
        if (s.Druh is null)
        {
            return null;
        }

        if (s.Druh == DruhStopy.Video && s.Sirka is > 0 && s.Vyska is > 0 && s.PomerStran is null)
        {
            s.PomerStran = s.Sirka.Value / (double)s.Vyska.Value;
        }

        if (s.VelkostStreamu is { } b && s.DlzkaSekund is > 0)
        {
            s.DatovyTok ??= (long)Math.Round(b * 8 / s.DlzkaSekund.Value);
        }

        return s;
    }

    private static string? Mdia(byte[] mdia, InfoStopy s)
    {
        string? handler = null;
        uint mierka = 0;
        foreach (var (typ, data) in Boxy(mdia))
        {
            switch (typ)
            {
                case "mdhd" when data.Length >= 24:
                    var verzia1 = data[0] == 1;
                    mierka = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(verzia1 ? 20 : 12));
                    double trvanie = verzia1 ? BinaryPrimitives.ReadUInt64BigEndian(data.AsSpan(24)) : BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(16));
                    if (mierka > 0 && trvanie > 0)
                    {
                        s.DlzkaSekund = trvanie / mierka;
                    }

                    var jazyk = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(verzia1 ? 32 : 20));
                    if (jazyk is > 0 and < 0x8000)
                    {
                        var kod = new string([(char)(((jazyk >> 10) & 0x1F) + 0x60), (char)(((jazyk >> 5) & 0x1F) + 0x60), (char)((jazyk & 0x1F) + 0x60)]);
                        s.Jazyk = Jazyky.Normalizuj(kod);
                    }

                    break;
                case "hdlr" when data.Length >= 12:
                    handler = Encoding.ASCII.GetString(data, 8, 4);
                    break;
                case "minf":
                    foreach (var (mtyp, mdata) in Boxy(data))
                    {
                        if (mtyp == "stbl")
                        {
                            Stbl(mdata, s, mierka);
                        }
                    }

                    break;
            }
        }

        return handler;
    }

    private static void Stbl(byte[] stbl, InfoStopy s, uint mierka)
    {
        foreach (var (typ, d) in Boxy(stbl))
        {
            switch (typ)
            {
                case "stsd" when d.Length > 16:
                    // the first sample entry
                    var dlzka = (int)BinaryPrimitives.ReadUInt32BigEndian(d.AsSpan(8));
                    if (dlzka >= 16 && 8 + dlzka <= d.Length)
                    {
                        Popis(Encoding.ASCII.GetString(d, 12, 4), d.AsSpan(16, dlzka - 8).ToArray(), s);
                    }

                    break;
                case "stts" when d.Length >= 8:
                    // constant frame rate: one entry of equal durations
                    var pocet = BinaryPrimitives.ReadUInt32BigEndian(d.AsSpan(4));
                    if (pocet >= 1 && d.Length >= 8 + (8 * (int)Math.Min(pocet, 4096)) && mierka > 0)
                    {
                        long vzoriek = 0;
                        long cas = 0;
                        for (var i = 0; i < pocet && 16 + (8 * i) <= d.Length; i++)
                        {
                            vzoriek += BinaryPrimitives.ReadUInt32BigEndian(d.AsSpan(8 + (8 * i)));
                            cas += (long)BinaryPrimitives.ReadUInt32BigEndian(d.AsSpan(8 + (8 * i))) * BinaryPrimitives.ReadUInt32BigEndian(d.AsSpan(12 + (8 * i)));
                        }

                        if (vzoriek > 0 && cas > 0)
                        {
                            s.Snimky = vzoriek * (double)mierka / cas;
                            s.KonstantneSnimky = pocet == 1;
                        }
                    }

                    break;
                case "stsz" when d.Length >= 12:
                    var velkost = BinaryPrimitives.ReadUInt32BigEndian(d.AsSpan(4));
                    var kusov = BinaryPrimitives.ReadUInt32BigEndian(d.AsSpan(8));
                    s.PocetSnimok = kusov;
                    if (velkost > 0)
                    {
                        s.VelkostStreamu = (long)velkost * kusov;
                    }
                    else
                    {
                        long sucet = 0;
                        for (var i = 0; i < kusov && 16 + (4 * i) <= d.Length; i++)
                        {
                            sucet += BinaryPrimitives.ReadUInt32BigEndian(d.AsSpan(12 + (4 * i)));
                        }

                        s.VelkostStreamu = sucet;
                    }

                    break;
            }
        }

    }

    /// <summary>A sample entry: the codec, picture or sound, and the codec's configuration boxes.</summary>
    private static void Popis(string format, byte[] d, InfoStopy s)
    {
        s.IdKodeku = format.Trim();
        (s.Kodek, s.Format, s.FormatInfo, s.ObchodnyNazov) = format switch
        {
            "hvc1" or "hev1" or "dvh1" or "dvhe" => ("hevc", "HEVC", "High Efficiency Video Coding", null),
            "avc1" or "avc3" or "dva1" or "dvav" => ("h264", "AVC", "Advanced Video Codec", null),
            "av01" => ("av1", "AV1", "AOMedia Video 1", null),
            "vp09" => ("vp9", "VP9", null, null),
            "mp4v" => ("mpeg4", "MPEG-4 Visual", null, null),
            "mp4a" => ("aac", "AAC", "Advanced Audio Codec", null),
            "ac-3" => ("ac3", "AC-3", "Audio Coding 3", "Dolby Digital"),
            "ec-3" => ("eac3", "E-AC-3", "Enhanced AC-3", "Dolby Digital Plus"),
            "mlpa" => ("truehd", "MLP FBA", "Meridian Lossless Packing FBA", "Dolby TrueHD"),
            "dtsc" => ("dts", "DTS", "Digital Theater Systems", "DTS"),
            "dtsh" or "dtsl" => ("dts-hd ma", "DTS XLL", "DTS-HD Master Audio", "DTS-HD Master Audio"),
            "Opus" => ("opus", "Opus", null, null),
            "fLaC" => ("flac", "FLAC", "Free Lossless Audio Codec", null),
            ".mp3" => ("mp3", "MPEG Audio", "MPEG-1 Layer 3", "MP3"),
            "tx3g" => ("mov_text", "Timed Text", null, null),
            "wvtt" => ("webvtt", "WebVTT", null, null),
            "stpp" => ("ttml", "TTML", null, null),
            "c608" => ("eia_608", "EIA-608", null, null),
            "jpeg" => ("mjpeg", "JPEG", null, null),
            "png " => ("png", "PNG", null, null),
            _ => (format.Trim().ToLowerInvariant(), format.Trim(), null, null),
        };

        int zaciatok;
        if (format is "hvc1" or "hev1" or "dvh1" or "dvhe" or "avc1" or "avc3" or "dva1" or "dvav" or "av01" or "vp09" or "mp4v" or "jpeg" or "png ")
        {
            if (d.Length < 78)
            {
                return;
            }

            s.Sirka = BinaryPrimitives.ReadUInt16BigEndian(d.AsSpan(24));
            s.Vyska = BinaryPrimitives.ReadUInt16BigEndian(d.AsSpan(26));
            zaciatok = 78;
        }
        else if (format is "mp4a" or "ac-3" or "ec-3" or "mlpa" or "dtsc" or "dtsh" or "dtsl" or "Opus" or "fLaC" or ".mp3")
        {
            if (d.Length < 28)
            {
                return;
            }

            var verzia = BinaryPrimitives.ReadUInt16BigEndian(d.AsSpan(8));
            s.Kanaly = BinaryPrimitives.ReadUInt16BigEndian(d.AsSpan(16));
            s.Vzorkovanie = BinaryPrimitives.ReadUInt32BigEndian(d.AsSpan(24)) >> 16;
            zaciatok = verzia switch { 1 => 44, 2 => 64, _ => 28 };
        }
        else
        {
            return;
        }

        if (zaciatok > d.Length)
        {
            return;
        }

        foreach (var (typ, data) in Boxy(d[zaciatok..]))
        {
            switch (typ)
            {
                case "hvcC":
                    KodekyVidea.Hevc(data, s);
                    break;
                case "avcC":
                    KodekyVidea.Avc(data, s);
                    break;
                case "av1C":
                    KodekyVidea.Av1(data, s);
                    break;
                case "colr" when data.Length >= 10 && Encoding.ASCII.GetString(data, 0, 4) is "nclx" or "nclc":
                    KodekyVidea.Farby(s, BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(4)), BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(6)),
                        BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(8)));
                    if (data.Length >= 11 && Encoding.ASCII.GetString(data, 0, 4) == "nclx")
                    {
                        s.FarebnyRozsah = (data[10] & 0x80) != 0 ? "Full" : "Limited";
                    }

                    break;
                case "pasp" when data.Length >= 8 && s.Sirka is > 0 && s.Vyska is > 0:
                    var h = BinaryPrimitives.ReadUInt32BigEndian(data);
                    var v = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(4));
                    if (h > 0 && v > 0)
                    {
                        s.PomerStran = s.Sirka.Value * (double)h / v / s.Vyska.Value;
                    }

                    break;
                case "dvcC" or "dvvC" when data.Length >= 4:
                    s.Hdr = Hdr.DolbyVision;
                    s.HdrPopis = $"Dolby Vision, verzia {data[0]}.{data[1]}, profil {data[2] >> 1}, úroveň {((data[2] & 1) << 5) | (data[3] >> 3)}";
                    break;
                case "btrt" when data.Length >= 12:
                    var priemer = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(8));
                    if (priemer > 0)
                    {
                        s.DatovyTok = priemer;
                    }

                    break;
                case "esds":
                    s.Profil = ProfilZEsds(data) ?? s.Profil;
                    break;
            }
        }
    }

    /// <summary>The AAC profile from the decoder specific info of an esds box.</summary>
    private static string? ProfilZEsds(byte[] esds)
    {
        // tag 0x05 (DecSpecificInfo) carries the AudioSpecificConfig
        for (var i = 4; i + 2 < esds.Length; i++)
        {
            if (esds[i] != 0x05)
            {
                continue;
            }

            var j = i + 1;
            var dlzka = 0;
            for (var k = 0; k < 4 && j < esds.Length; k++, j++)
            {
                dlzka = (dlzka << 7) | (esds[j] & 0x7F);
                if ((esds[j] & 0x80) == 0)
                {
                    j++;
                    break;
                }
            }

            if (dlzka > 0 && j + dlzka <= esds.Length)
            {
                return KodekyVidea.ProfilAac(esds.AsSpan(j, dlzka));
            }
        }

        return null;
    }

    /// <summary>The boxes inside a box read into memory.</summary>
    private static IEnumerable<(string Typ, byte[] Data)> Boxy(byte[] obsah)
    {
        var pos = 0;
        while (pos + 8 <= obsah.Length)
        {
            long velkost = BinaryPrimitives.ReadUInt32BigEndian(obsah.AsSpan(pos));
            var typ = Encoding.Latin1.GetString(obsah, pos + 4, 4);
            var hlavicka = 8;
            if (velkost == 1 && pos + 16 <= obsah.Length)
            {
                velkost = (long)BinaryPrimitives.ReadUInt64BigEndian(obsah.AsSpan(pos + 8));
                hlavicka = 16;
            }
            else if (velkost == 0)
            {
                velkost = obsah.Length - pos;
            }

            if (velkost < hlavicka || pos + velkost > obsah.Length)
            {
                yield break;
            }

            yield return (typ, obsah.AsSpan(pos + hlavicka, (int)velkost - hlavicka).ToArray());
            pos += (int)velkost;
        }
    }
}
