using System.Buffers.Binary;
using System.Globalization;
using System.Text;
using Katalog.Jadro.Model;

namespace Katalog.Media;

/// <summary>
/// KATALOG's own reader of AVI files (RIFF, also OpenDML over 1 GB): the header list (avih, strl with strh, strf, strn),
/// the first chunk of each stream (the VOL of MPEG-4 Visual with Xvid or DivX, the SPS of H.264, the first audio frame)
/// and the old index (idx1) for the exact size of each stream; without it the sizes are estimated.
/// </summary>
public static class CitanieAvi
{
    private const int NajviacHlavicka = 2 << 20;
    private const long NajviacIndex = 16L << 20;

    public static InfoSuboru? Citaj(string cesta)
    {
        try
        {
            using var subor = new FileStream(cesta, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 1 << 16, FileOptions.RandomAccess);
            return Citaj(subor);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
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

    private sealed class Prud
    {
        public required InfoStopy Stopa { get; init; }

        public uint Mierka { get; set; }

        public uint Rychlost { get; set; }

        public uint Dlzka { get; set; }

        public uint VelkostVzorky { get; set; }

        public uint BajtovZaSekundu { get; set; }

        public byte[]? PrvyKus { get; set; }

        public long Bajtov { get; set; }
    }

    private static InfoSuboru? CitajBezpecne(Stream subor)
    {
        Span<byte> hlava = stackalloc byte[12];
        subor.Position = 0;
        if (subor.ReadAtLeast(hlava, 12, throwOnEndOfStream: false) < 12 || !hlava[..4].SequenceEqual("RIFF"u8) || !hlava.Slice(8, 4).SequenceEqual("AVI "u8))
        {
            return null;
        }

        var info = new InfoSuboru { Velkost = subor.Length, Format = "AVI", VerziaFormatu = "RIFF" };
        var prudy = new List<Prud>();
        long? movi = null, moviKoniec = null;
        long pos = 12;
        var kusov = 0;
        uint mikrosekund = 0;
        uint dmlhSnimok = 0;
        var openDml = false;
        while (pos + 8 <= subor.Length)
        {
            var (id, velkost) = Kus(subor, pos);
            if (id == "LIST" && pos + 12 <= subor.Length)
            {
                var typ = Text(subor, pos + 8, 4);
                if (typ == "hdrl" && velkost <= NajviacHlavicka)
                {
                    var hdrl = Precitaj(subor, pos + 12, (int)velkost - 4);
                    Hlavicky(hdrl, prudy, ref mikrosekund, ref dmlhSnimok, ref openDml);
                }
                else if (typ == "movi")
                {
                    movi ??= pos + 12;
                    moviKoniec ??= pos + 8 + velkost;
                }
                else if (typ == "INFO" && velkost <= 65536)
                {
                    InfoZoznam(Precitaj(subor, pos + 12, (int)velkost - 4), info);
                }
            }
            else if (id == "idx1" && velkost <= NajviacIndex && movi is not null)
            {
                Index(Precitaj(subor, pos + 8, (int)velkost), prudy);
            }
            else if (id == "RIFF")
            {
                openDml = true;
                pos += 12; // AVIX: into it, its movi list
                continue;
            }

            if (velkost < 0 || pos + 8 + velkost <= pos)
            {
                break;
            }

            pos += 8 + velkost + (velkost & 1);
            if (++kusov > 10_000)
            {
                break;
            }
        }

        if (prudy.Count == 0)
        {
            return null;
        }

        if (openDml)
        {
            info.VerziaFormatu = "OpenDML (AVI 2.0)";
        }

        if (movi is { } m)
        {
            PrveKusy(subor, m, moviKoniec ?? subor.Length, prudy);
        }

        var poradie = 0;
        var video = prudy.FirstOrDefault(p => p.Stopa.JeVideo);
        foreach (var p in prudy)
        {
            var s = p.Stopa;
            s.Id = poradie++;
            if (p.Rychlost > 0 && p.Mierka > 0)
            {
                var dlzka = p.Dlzka;
                if (s.JeVideo && dmlhSnimok > dlzka)
                {
                    dlzka = dmlhSnimok;
                }

                if (s.JeVideo || p.VelkostVzorky == 0)
                {
                    s.DlzkaSekund = dlzka * (double)p.Mierka / p.Rychlost;
                }
                else if (p.BajtovZaSekundu > 0)
                {
                    s.DlzkaSekund = dlzka * (double)p.VelkostVzorky / p.BajtovZaSekundu;
                }

                if (s.JeVideo)
                {
                    s.Snimky = p.Rychlost / (double)p.Mierka;
                    s.KonstantneSnimky = true;
                    s.PocetSnimok = dlzka;
                }
            }

            if (p.PrvyKus is { } kus)
            {
                if (s.Kodek == "mpeg4")
                {
                    Mpeg4Visual(kus, s);
                }
                else
                {
                    ElementarneToky.Analyzuj(kus, s);
                }
            }
        }

        info.DlzkaSekund = video?.Stopa.DlzkaSekund ?? (mikrosekund > 0 && dmlhSnimok > 0 ? dmlhSnimok * mikrosekund / 1e6 : prudy.Max(p => p.Stopa.DlzkaSekund));
        var znamyIndex = prudy.Any(p => p.Bajtov > 0);
        foreach (var p in prudy)
        {
            var s = p.Stopa;
            if (znamyIndex && p.Bajtov > 0 && !openDml)
            {
                s.VelkostStreamu = p.Bajtov;
            }
            else if (!s.JeVideo && p.BajtovZaSekundu > 0 && s.DlzkaSekund is > 0)
            {
                s.VelkostStreamu = (long)(p.BajtovZaSekundu * s.DlzkaSekund.Value);
                s.Odhad = true;
            }

            s.DlzkaSekund ??= info.DlzkaSekund;
            if (s.DatovyTok is null && s.VelkostStreamu is { } b && s.DlzkaSekund is > 0)
            {
                s.DatovyTok = (long)(b * 8 / s.DlzkaSekund.Value);
            }
        }

        // the video takes what the other streams leave (when the index did not say)
        if (video is { Stopa.VelkostStreamu: null } && movi is not null)
        {
            var ostatne = prudy.Where(p => p != video).Sum(p => p.Stopa.VelkostStreamu ?? 0);
            video.Stopa.VelkostStreamu = (long)((info.Velkost * 0.98) - ostatne);
            video.Stopa.Odhad = true;
            if (video.Stopa.DlzkaSekund is > 0)
            {
                video.Stopa.DatovyTok = (long)(video.Stopa.VelkostStreamu * 8 / video.Stopa.DlzkaSekund.Value);
                video.Stopa.OdhadToku = true;
            }
        }

        foreach (var p in prudy)
        {
            info.Stopy.Add(p.Stopa);
        }

        return info;
    }

    /// <summary>The header list: avih, then a strl for each stream (strh, strf, strn), and OpenDML's dmlh.</summary>
    private static void Hlavicky(byte[] hdrl, List<Prud> prudy, ref uint mikrosekund, ref uint dmlhSnimok, ref bool openDml)
    {
        foreach (var (id, data) in Kusy(hdrl))
        {
            if (id == "avih" && data.Length >= 40)
            {
                mikrosekund = BinaryPrimitives.ReadUInt32LittleEndian(data);
            }
            else if (id == "LIST" && data.Length >= 4 && Encoding.ASCII.GetString(data, 0, 4) == "strl")
            {
                if (CitajPrud(data[4..]) is { } prud)
                {
                    prudy.Add(prud);
                }
            }
            else if (id == "LIST" && data.Length >= 4 && Encoding.ASCII.GetString(data, 0, 4) == "odml")
            {
                openDml = true;
                foreach (var (oid, odata) in Kusy(data[4..]))
                {
                    if (oid == "dmlh" && odata.Length >= 4)
                    {
                        dmlhSnimok = BinaryPrimitives.ReadUInt32LittleEndian(odata);
                    }
                }
            }
        }
    }

    private static Prud? CitajPrud(byte[] strl)
    {
        byte[]? strh = null, strf = null;
        string? nazov = null;
        foreach (var (id, data) in Kusy(strl))
        {
            switch (id)
            {
                case "strh":
                    strh = data;
                    break;
                case "strf":
                    strf = data;
                    break;
                case "strn":
                    nazov = Encoding.Latin1.GetString(data).TrimEnd('\0').Trim();
                    break;
            }
        }

        if (strh is not { Length: >= 48 })
        {
            return null;
        }

        var typ = Encoding.ASCII.GetString(strh, 0, 4);
        var s = new InfoStopy { Nazov = string.IsNullOrEmpty(nazov) ? null : nazov, Predvolena = true };
        var prud = new Prud
        {
            Stopa = s,
            Mierka = BinaryPrimitives.ReadUInt32LittleEndian(strh.AsSpan(20)),
            Rychlost = BinaryPrimitives.ReadUInt32LittleEndian(strh.AsSpan(24)),
            Dlzka = BinaryPrimitives.ReadUInt32LittleEndian(strh.AsSpan(32)),
            VelkostVzorky = BinaryPrimitives.ReadUInt32LittleEndian(strh.AsSpan(44)),
        };
        if (typ == "vids" && strf is { Length: >= 20 })
        {
            s.Druh = DruhStopy.Video;
            s.JeVideo = true;
            s.Sirka = Math.Abs(BinaryPrimitives.ReadInt32LittleEndian(strf.AsSpan(4)));
            s.Vyska = Math.Abs(BinaryPrimitives.ReadInt32LittleEndian(strf.AsSpan(8)));
            var fourcc = Encoding.ASCII.GetString(strf, 16, 4);
            s.IdKodeku = fourcc.TrimEnd('\0', ' ');
            (s.Kodek, s.Format, s.FormatInfo) = fourcc.ToUpperInvariant() switch
            {
                "XVID" => ("mpeg4", "MPEG-4 Visual", "Xvid"),
                "DIVX" or "DX50" or "DIV4" or "DIV5" => ("mpeg4", "MPEG-4 Visual", "DivX"),
                "FMP4" or "MP4V" or "M4S2" or "3IV2" => ("mpeg4", "MPEG-4 Visual", null),
                "DIV3" or "MP43" => ("msmpeg4v3", "MPEG-4 Visual", "DivX 3 (MS MPEG-4 v3)"),
                "H264" or "X264" or "AVC1" or "DAVC" or "VSSH" => ("h264", "AVC", "Advanced Video Codec"),
                "HEVC" or "H265" or "HVC1" or "X265" => ("hevc", "HEVC", "High Efficiency Video Coding"),
                "MJPG" or "AVRN" or "LJPG" => ("mjpeg", "JPEG", "Motion JPEG"),
                "WMV3" => ("wmv3", "VC-1", "Windows Media Video 9"),
                "WVC1" => ("vc1", "VC-1", null),
                "MPG2" or "MPEG" or "\0\0\0\u0010" => ("mpeg2video", "MPEG Video", null),
                "VP80" => ("vp8", "VP8", null),
                "VP90" => ("vp9", "VP9", null),
                "DVSD" or "DV25" or "DV50" or "DVHD" or "CDVC" => ("dvvideo", "DV", null),
                "FFV1" => ("ffv1", "FFV1", null),
                "HFYU" or "FFVH" => ("huffyuv", "HuffYUV", null),
                "\0\0\0\0" or "DIB " => ("rawvideo", "RGB", "nekomprimované video"),
                _ => (fourcc.Trim('\0', ' ').ToLowerInvariant(), fourcc.Trim('\0', ' '), null),
            };
            s.FarebnyPriestor = s.Kodek == "rawvideo" ? "RGB" : "YUV";
            if (s.Sirka > 0 && s.Vyska > 0)
            {
                s.PomerStran = s.Sirka.Value / (double)s.Vyska.Value;
            }
        }
        else if (typ == "auds" && strf is { Length: >= 16 })
        {
            s.Druh = DruhStopy.Zvuk;
            var znacka = BinaryPrimitives.ReadUInt16LittleEndian(strf);
            if (znacka == 0xFFFE && strf.Length >= 26)
            {
                znacka = BinaryPrimitives.ReadUInt16LittleEndian(strf.AsSpan(24)); // WAVE_FORMAT_EXTENSIBLE: the subformat
            }

            s.Kanaly = BinaryPrimitives.ReadUInt16LittleEndian(strf.AsSpan(2));
            s.Vzorkovanie = BinaryPrimitives.ReadUInt32LittleEndian(strf.AsSpan(4));
            prud.BajtovZaSekundu = BinaryPrimitives.ReadUInt32LittleEndian(strf.AsSpan(8));
            var bity = BinaryPrimitives.ReadUInt16LittleEndian(strf.AsSpan(14));
            s.IdKodeku = "0x" + znacka.ToString("X4", CultureInfo.InvariantCulture);
            (s.Kodek, s.Format, s.FormatInfo, s.ObchodnyNazov) = znacka switch
            {
                0x0001 => ("pcm_s16le", "PCM", "Little Endian", (string?)null),
                0x0003 => ("pcm_f32le", "PCM", "Float (IEEE)", null),
                0x0050 => ("mp2", "MPEG Audio", null, null),
                0x0055 => ("mp3", "MPEG Audio", "MPEG-1 Layer 3", "MP3"),
                0x00FF or 0x1600 or 0x1610 or 0x706D or 0x4143 => ("aac", "AAC", "Advanced Audio Codec", null),
                0x2000 => ("ac3", "AC-3", "Audio Coding 3", "Dolby Digital"),
                0x2001 => ("dts", "DTS", "Digital Theater Systems", "DTS"),
                0x0161 => ("wmav2", "WMA", "Windows Media Audio", null),
                0x0162 => ("wmapro", "WMA", "Windows Media Audio Pro", null),
                0x0163 => ("wmalossless", "WMA", "Windows Media Audio Lossless", null),
                0x0011 => ("adpcm_ima_wav", "ADPCM", "IMA", null),
                0x0002 => ("adpcm_ms", "ADPCM", "Microsoft", null),
                0x674F or 0x6750 or 0x6751 => ("vorbis", "Vorbis", null, null),
                0xF1AC => ("flac", "FLAC", null, null),
                _ => ($"0x{znacka:X4}", $"0x{znacka:X4}", null, null),
            };
            if (s.Format == "PCM")
            {
                s.BitovaHlbka = bity;
                s.KonstantnyTok = true;
            }

            s.DatovyTok = prud.BajtovZaSekundu > 0 ? prud.BajtovZaSekundu * 8L : null;
            if (s.Kodek is "mp3" && prud.VelkostVzorky == 0)
            {
                s.KonstantnyTok = false;
            }
        }
        else if (typ == "txts")
        {
            s.Druh = DruhStopy.Titulky;
            s.Kodek = "text";
            s.Format = "Text";
        }
        else
        {
            return null;
        }

        return prud;
    }

    /// <summary>The first chunk of each stream in the movi list (00dc / 01wb …), at most 256 kB each.</summary>
    private static void PrveKusy(Stream subor, long pos, long koniec, List<Prud> prudy)
    {
        var hranica = Math.Min(koniec, pos + (16L << 20));
        var chybaju = prudy.Count(p => p.Stopa.Druh is DruhStopy.Video or DruhStopy.Zvuk);
        while (pos + 8 <= hranica && chybaju > 0)
        {
            var (id, velkost) = Kus(subor, pos);
            if (id == "LIST")
            {
                pos += 12; // a rec list: into it
                continue;
            }

            if (velkost < 0)
            {
                return;
            }

            if (id.Length == 4 && char.IsAsciiDigit(id[0]) && char.IsAsciiDigit(id[1]) && velkost > 0)
            {
                var cislo = ((id[0] - '0') * 10) + (id[1] - '0');
                if (cislo < prudy.Count && prudy[cislo].PrvyKus is null)
                {
                    prudy[cislo].PrvyKus = Precitaj(subor, pos + 8, (int)Math.Min(velkost, 256 << 10));
                    chybaju--;
                }
            }

            pos += 8 + velkost + (velkost & 1);
        }
    }

    /// <summary>The old index: the size of each stream as the sum of its chunks.</summary>
    private static void Index(byte[] idx1, List<Prud> prudy)
    {
        for (var i = 0; i + 16 <= idx1.Length; i += 16)
        {
            if (char.IsAsciiDigit((char)idx1[i]) && char.IsAsciiDigit((char)idx1[i + 1]))
            {
                var cislo = ((idx1[i] - '0') * 10) + (idx1[i + 1] - '0');
                if (cislo < prudy.Count)
                {
                    prudy[cislo].Bajtov += BinaryPrimitives.ReadUInt32LittleEndian(idx1.AsSpan(i + 12));
                }
            }
        }
    }

    /// <summary>The INFO list: the title (INAM) and the program that wrote the file (ISFT).</summary>
    private static void InfoZoznam(byte[] data, InfoSuboru info)
    {
        foreach (var (id, hodnota) in Kusy(data))
        {
            var text = Encoding.Latin1.GetString(hodnota).TrimEnd('\0').Trim();
            if (text.Length == 0)
            {
                continue;
            }

            if (id == "INAM")
            {
                info.Nazov = text;
            }
            else if (id == "ISFT")
            {
                info.Aplikacia = text;
            }
        }
    }

    /// <summary>
    /// MPEG-4 Visual (Xvid, DivX): the profile and level of the visual object sequence (00 00 01 B0) and the encoder
    /// from the user data (00 00 01 B2, "XviD0050", "DivX503b1393p").
    /// </summary>
    internal static void Mpeg4Visual(ReadOnlySpan<byte> data, InfoStopy s)
    {
        var vos = data.IndexOf([(byte)0, (byte)0, (byte)1, (byte)0xB0]);
        if (vos >= 0 && vos + 4 < data.Length)
        {
            var pl = data[vos + 4];
            s.Profil = pl switch
            {
                >= 0xF0 and <= 0xF5 => "Advanced Simple@L" + (pl & 0x0F).ToString(CultureInfo.InvariantCulture),
                0xF7 => "Advanced Simple@L3b",
                >= 0x01 and <= 0x03 => "Simple@L" + pl.ToString(CultureInfo.InvariantCulture),
                0x08 => "Simple@L0",
                _ => s.Profil,
            };
        }

        // the video object layer (00 00 01 20–2F): the pixel aspect ratio
        for (var i = 0; i + 8 < data.Length; i++)
        {
            if (data[i] != 0 || data[i + 1] != 0 || data[i + 2] != 1 || data[i + 3] is < 0x20 or > 0x2F)
            {
                continue;
            }

            var r = new CitacBitov(data.Slice(i + 4, 4).ToArray());
            r.Preskoc(1 + 8);
            if (r.Bit() == 1)
            {
                r.Preskoc(4 + 3);
            }

            var par = (int)r.Bity(4);
            (double w, double h) pomer = par switch
            {
                2 => (12, 11), 3 => (10, 11), 4 => (16, 11), 5 => (40, 33),
                15 => (r.Bity(8), r.Bity(8)),
                _ => (1, 1),
            };
            if (pomer.w > 0 && pomer.h > 0 && s.Sirka is > 0 && s.Vyska is > 0)
            {
                s.PomerStran = s.Sirka.Value * pomer.w / (s.Vyska.Value * pomer.h);
            }

            break;
        }

        var uzivatel = data.IndexOf([(byte)0, (byte)0, (byte)1, (byte)0xB2]);
        if (uzivatel >= 0)
        {
            var koniec = data[(uzivatel + 4)..].IndexOf([(byte)0, (byte)0, (byte)1]);
            var text = Encoding.ASCII.GetString(data.Slice(uzivatel + 4, koniec < 0 ? Math.Min(40, data.Length - uzivatel - 4) : Math.Min(koniec, 40)));
            if (text.StartsWith("XviD", StringComparison.Ordinal))
            {
                s.FormatInfo = "Xvid (build " + new string(text[4..].TakeWhile(char.IsAsciiDigit).ToArray()).TrimStart('0') + ")";
            }
            else if (text.StartsWith("DivX", StringComparison.Ordinal))
            {
                s.FormatInfo = "DivX " + new string(text[4..].TakeWhile(char.IsAsciiDigit).ToArray());
                if (text.Contains('p', StringComparison.Ordinal))
                {
                    s.FormatInfo += " (packed bitstream)";
                }
            }
        }

        s.Podvzorkovanie ??= "4:2:0";
        s.BitovaHlbka ??= 8;
        s.Prekladane ??= false;
    }

    private static (string Id, long Velkost) Kus(Stream subor, long pos)
    {
        Span<byte> h = stackalloc byte[8];
        subor.Position = pos;
        if (subor.ReadAtLeast(h, 8, throwOnEndOfStream: false) < 8)
        {
            return ("", -1);
        }

        return (Encoding.Latin1.GetString(h[..4]), BinaryPrimitives.ReadUInt32LittleEndian(h[4..]));
    }

    private static IEnumerable<(string Id, byte[] Data)> Kusy(byte[] obsah)
    {
        var pos = 0;
        while (pos + 8 <= obsah.Length)
        {
            var id = Encoding.Latin1.GetString(obsah, pos, 4);
            var velkost = (int)Math.Min(BinaryPrimitives.ReadUInt32LittleEndian(obsah.AsSpan(pos + 4)), (uint)(obsah.Length - pos - 8));
            yield return (id, obsah.AsSpan(pos + 8, velkost).ToArray());
            pos += 8 + velkost + (velkost & 1);
        }
    }

    private static string Text(Stream subor, long pos, int dlzka) => Encoding.Latin1.GetString(Precitaj(subor, pos, dlzka));

    private static byte[] Precitaj(Stream subor, long pozicia, int dlzka)
    {
        var data = new byte[(int)Math.Max(0, Math.Min(dlzka, subor.Length - pozicia))];
        subor.Position = pozicia;
        subor.ReadExactly(data);
        return data;
    }
}
