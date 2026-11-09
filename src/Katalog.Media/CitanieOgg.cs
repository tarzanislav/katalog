using System.Buffers.Binary;
using System.Text;
using Katalog.Jadro.Model;

namespace Katalog.Media;

/// <summary>
/// KATALOG's own reader of Ogg (OGV, OGM, OGG): the identification headers in the first pages of each logical stream
/// (Theora, Vorbis, Opus, FLAC, Speex, and the video and audio of OGM), the language and title from the comments, and
/// the last granule positions at the end for the length.
/// </summary>
public static class CitanieOgg
{
    private const int Zaciatok = 256 << 10;
    private const int Koniec = 256 << 10;

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

    private sealed class Prud(InfoStopy stopa)
    {
        public InfoStopy Stopa { get; } = stopa;

        /// <summary>How a granule position becomes seconds (Theora: frames with a keyframe shift; audio: samples).</summary>
        public Func<long, double>? Cas { get; set; }

        public long? PoslednaPozicia { get; set; }

        public bool Komentar { get; set; }
    }

    private static InfoSuboru? CitajBezpecne(Stream subor)
    {
        var zaciatok = Precitaj(subor, 0, Zaciatok);
        if (zaciatok.Length < 28 || !zaciatok.AsSpan(0, 4).SequenceEqual("OggS"u8))
        {
            return null;
        }

        var prudy = new Dictionary<uint, Prud>();
        foreach (var (seriove, pozicia, pakety) in Stranky(zaciatok))
        {
            foreach (var paket in pakety)
            {
                if (!prudy.TryGetValue(seriove, out var prud))
                {
                    if (Identifikacia(paket) is { } novy)
                    {
                        prudy[seriove] = novy;
                    }
                }
                else if (!prud.Komentar && Komentare(paket, prud.Stopa))
                {
                    prud.Komentar = true;
                }
            }
        }

        if (prudy.Count == 0)
        {
            return null;
        }

        foreach (var (seriove, pozicia, _) in Stranky(Precitaj(subor, Math.Max(0, subor.Length - Koniec), Koniec)))
        {
            if (prudy.TryGetValue(seriove, out var prud) && pozicia > 0)
            {
                prud.PoslednaPozicia = pozicia;
            }
        }

        var info = new InfoSuboru { Velkost = subor.Length, Format = "Ogg" };
        var cislo = 0;
        foreach (var prud in prudy.Values)
        {
            var s = prud.Stopa;
            s.Id = cislo++;
            if (prud.PoslednaPozicia is { } p && prud.Cas is { } cas)
            {
                s.DlzkaSekund = cas(p);
            }

            info.Stopy.Add(s);
        }

        info.DlzkaSekund = info.Stopy.Where(s => s.DlzkaSekund is > 0).Select(s => s.DlzkaSekund).DefaultIfEmpty().Max();
        info.VerziaFormatu = info.Stopy.Any(s => s.Kodek == "theora") ? "Ogg Video (OGV)" : info.Stopy.Any(s => s.IdKodeku?.StartsWith("ogm", StringComparison.Ordinal) == true) ? "OGM" : null;
        foreach (var s in info.Stopy)
        {
            s.DlzkaSekund ??= info.DlzkaSekund;
        }

        return info;
    }

    /// <summary>The first packet of a logical stream tells its codec.</summary>
    private static Prud? Identifikacia(byte[] p)
    {
        var s = new InfoStopy { Predvolena = true };
        var prud = new Prud(s);
        if (p.Length >= 42 && p[0] == 0x80 && p.AsSpan(1, 6).SequenceEqual("theora"u8))
        {
            (s.Druh, s.JeVideo, s.Kodek, s.Format, s.IdKodeku) = (DruhStopy.Video, true, "theora", "Theora", "theora");
            s.Sirka = (p[14] << 16) | (p[15] << 8) | p[16];
            s.Vyska = (p[17] << 16) | (p[18] << 8) | p[19];
            var citatel = BinaryPrimitives.ReadUInt32BigEndian(p.AsSpan(22));
            var menovatel = BinaryPrimitives.ReadUInt32BigEndian(p.AsSpan(26));
            var parW = (p[30] << 16) | (p[31] << 8) | p[32];
            var parH = (p[33] << 16) | (p[34] << 8) | p[35];
            if (citatel > 0 && menovatel > 0)
            {
                s.Snimky = citatel / (double)menovatel;
            }

            if (s.Sirka > 0 && s.Vyska > 0)
            {
                s.PomerStran = s.Sirka.Value * (parW > 0 && parH > 0 ? parW / (double)parH : 1) / s.Vyska.Value;
            }

            var nominalny = (p[37] << 16) | (p[38] << 8) | p[39];
            s.DatovyTok = nominalny > 0 ? nominalny : null;
            var posun = ((p[40] & 3) << 3) | (p[41] >> 5);
            var fps = s.Snimky ?? 25;
            prud.Cas = g => ((g >> posun) + (g & ((1L << posun) - 1))) / fps;
            s.Podvzorkovanie = ((p[41] >> 3) & 3) switch { 2 => "4:2:2", 3 => "4:4:4", _ => "4:2:0" };
            s.FarebnyPriestor = "YUV";
            s.BitovaHlbka = 8;
            s.Prekladane = false;
        }
        else if (p.Length >= 30 && p[0] == 1 && p.AsSpan(1, 6).SequenceEqual("vorbis"u8))
        {
            (s.Druh, s.Kodek, s.Format, s.IdKodeku) = (DruhStopy.Zvuk, "vorbis", "Vorbis", "vorbis");
            s.Kanaly = p[11];
            s.Vzorkovanie = BinaryPrimitives.ReadUInt32LittleEndian(p.AsSpan(12));
            var nominalny = BinaryPrimitives.ReadInt32LittleEndian(p.AsSpan(20));
            s.DatovyTok = nominalny > 0 ? nominalny : null;
            var frekvencia = s.Vzorkovanie.Value;
            prud.Cas = g => frekvencia > 0 ? g / frekvencia : 0;
        }
        else if (p.Length >= 19 && p.AsSpan(0, 8).SequenceEqual("OpusHead"u8))
        {
            (s.Druh, s.Kodek, s.Format, s.IdKodeku) = (DruhStopy.Zvuk, "opus", "Opus", "opus");
            s.Kanaly = p[9];
            var predskok = BinaryPrimitives.ReadUInt16LittleEndian(p.AsSpan(10));
            var vstup = BinaryPrimitives.ReadUInt32LittleEndian(p.AsSpan(12));
            s.Vzorkovanie = vstup > 0 ? vstup : 48000;
            prud.Cas = g => (g - predskok) / 48000.0;
        }
        else if (p.Length >= 51 && p[0] == 0x7F && p.AsSpan(1, 4).SequenceEqual("FLAC"u8))
        {
            (s.Druh, s.Kodek, s.Format, s.IdKodeku) = (DruhStopy.Zvuk, "flac", "FLAC", "flac");
            var info = p.AsSpan(17); // STREAMINFO after "fLaC" and the block header
            s.Vzorkovanie = (info[10] << 12) | (info[11] << 4) | (info[12] >> 4);
            s.Kanaly = ((info[12] >> 1) & 7) + 1;
            s.BitovaHlbka = (((info[12] & 1) << 4) | (info[13] >> 4)) + 1;
            var frekvencia = s.Vzorkovanie.Value;
            prud.Cas = g => frekvencia > 0 ? g / frekvencia : 0;
        }
        else if (p.Length >= 64 && p.AsSpan(0, 8).SequenceEqual("Speex   "u8))
        {
            (s.Druh, s.Kodek, s.Format, s.IdKodeku) = (DruhStopy.Zvuk, "speex", "Speex", "speex");
            s.Vzorkovanie = BinaryPrimitives.ReadUInt32LittleEndian(p.AsSpan(36));
            s.Kanaly = (int)BinaryPrimitives.ReadUInt32LittleEndian(p.AsSpan(48));
            var frekvencia = s.Vzorkovanie.Value;
            prud.Cas = g => frekvencia > 0 ? g / frekvencia : 0;
        }
        else if (p.Length >= 57 && p[0] == 1 && (p.AsSpan(1, 5).SequenceEqual("video"u8) || p.AsSpan(1, 5).SequenceEqual("audio"u8)))
        {
            // OGM: stream type, subtype (FourCC or audio tag), time unit (100 ns), samples per unit, size …
            var video = p[1] == 'v';
            var podtyp = Encoding.ASCII.GetString(p, 9, 4).TrimEnd('\0', ' ');
            var jednotka = BinaryPrimitives.ReadInt64LittleEndian(p.AsSpan(17));
            var vzoriek = BinaryPrimitives.ReadInt64LittleEndian(p.AsSpan(25));
            s.IdKodeku = "ogm " + podtyp;
            if (video)
            {
                s.Druh = DruhStopy.Video;
                s.JeVideo = true;
                (s.Kodek, s.Format) = podtyp.ToUpperInvariant() switch
                {
                    "XVID" or "DIVX" or "DX50" or "FMP4" => ("mpeg4", "MPEG-4 Visual"),
                    "H264" or "AVC1" => ("h264", "AVC"),
                    _ => (podtyp.ToLowerInvariant(), podtyp),
                };
                s.Sirka = BinaryPrimitives.ReadInt32LittleEndian(p.AsSpan(45));
                s.Vyska = BinaryPrimitives.ReadInt32LittleEndian(p.AsSpan(49));
                if (jednotka > 0)
                {
                    s.Snimky = 1e7 / jednotka;
                    var fps = s.Snimky.Value;
                    prud.Cas = g => g / fps;
                }

                if (s.Sirka > 0 && s.Vyska > 0)
                {
                    s.PomerStran = s.Sirka.Value / (double)s.Vyska.Value;
                }
            }
            else
            {
                s.Druh = DruhStopy.Zvuk;
                var znacka = int.TryParse(podtyp, System.Globalization.NumberStyles.HexNumber, null, out var z) ? z : 0;
                (s.Kodek, s.Format) = znacka switch
                {
                    0x55 => ("mp3", "MPEG Audio"),
                    0x2000 => ("ac3", "AC-3"),
                    0x2001 => ("dts", "DTS"),
                    0xFF => ("aac", "AAC"),
                    0x01 => ("pcm_s16le", "PCM"),
                    _ => ($"0x{znacka:X4}", podtyp),
                };
                s.Kanaly = BinaryPrimitives.ReadInt16LittleEndian(p.AsSpan(45));
                s.Vzorkovanie = vzoriek > 0 ? vzoriek : null;
                s.DatovyTok = BinaryPrimitives.ReadInt32LittleEndian(p.AsSpan(49)) * 8L;
                var frekvencia = (double)vzoriek;
                prud.Cas = g => frekvencia > 0 ? g / frekvencia : 0;
            }
        }
        else if (p.Length >= 9 && p[0] == 1 && p.AsSpan(1, 4).SequenceEqual("text"u8))
        {
            (s.Druh, s.Kodek, s.Format, s.IdKodeku) = (DruhStopy.Titulky, "text", "Text", "ogm text");
        }
        else if (p.Length >= 8 && p.AsSpan(0, 8).SequenceEqual("CMML\0\0\0\0"u8))
        {
            return null;
        }
        else
        {
            return null;
        }

        return prud;
    }

    /// <summary>The comment header (Vorbis comment): LANGUAGE and TITLE of the stream; true when it was one.</summary>
    private static bool Komentare(byte[] p, InfoStopy s)
    {
        var zaciatok = p.Length >= 7 && p[0] is 3 or 0x81 && (p.AsSpan(1, 6).SequenceEqual("vorbis"u8) || p.AsSpan(1, 6).SequenceEqual("theora"u8)) ? 7
            : p.Length >= 8 && p.AsSpan(0, 8).SequenceEqual("OpusTags"u8) ? 8
            : p.Length >= 4 && (p[0] & 0x7F) == 4 ? 4 // FLAC: a metadata block VORBIS_COMMENT
            : p.Length >= 1 && p[0] == 3 ? 1 // OGM
            : -1;
        if (zaciatok < 0 || zaciatok + 8 > p.Length)
        {
            return false;
        }

        var i = zaciatok;
        var dodavatel = (int)Math.Min(BinaryPrimitives.ReadUInt32LittleEndian(p.AsSpan(i)), (uint)p.Length);
        i += 4 + dodavatel;
        if (i + 4 > p.Length)
        {
            return true;
        }

        var pocet = BinaryPrimitives.ReadUInt32LittleEndian(p.AsSpan(i));
        i += 4;
        for (var n = 0; n < pocet && i + 4 <= p.Length; n++)
        {
            var dlzka = (int)Math.Min(BinaryPrimitives.ReadUInt32LittleEndian(p.AsSpan(i)), (uint)(p.Length - i - 4));
            var text = Encoding.UTF8.GetString(p, i + 4, dlzka);
            i += 4 + dlzka;
            var rovna = text.IndexOf('=', StringComparison.Ordinal);
            if (rovna <= 0)
            {
                continue;
            }

            var kluc = text[..rovna].ToUpperInvariant();
            var hodnota = text[(rovna + 1)..].Trim();
            if (kluc == "LANGUAGE" && hodnota.Length > 0)
            {
                s.Jazyk = Jazyky.Normalizuj(hodnota.Split('-', '_')[0]) ?? s.Jazyk;
            }
            else if (kluc is "TITLE" && hodnota.Length > 0)
            {
                s.Nazov ??= hodnota;
            }
        }

        return true;
    }

    /// <summary>The pages of a piece of the file: serial number, granule position and the whole packets that end on them.</summary>
    private static List<(uint Seriove, long Pozicia, List<byte[]> Pakety)> Stranky(byte[] data)
    {
        var stranky = new List<(uint, long, List<byte[]>)>();
        var rozpracovane = new Dictionary<uint, List<byte>>();
        var i = 0;
        while (i + 27 <= data.Length)
        {
            if (!data.AsSpan(i, 4).SequenceEqual("OggS"u8))
            {
                var dalsi = data.AsSpan(i + 1).IndexOf("OggS"u8);
                if (dalsi < 0)
                {
                    break;
                }

                i += dalsi + 1;
                continue;
            }

            var pozicia = BinaryPrimitives.ReadInt64LittleEndian(data.AsSpan(i + 6));
            var seriove = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(i + 14));
            var segmentov = data[i + 26];
            if (i + 27 + segmentov > data.Length)
            {
                break;
            }

            var obsah = i + 27 + segmentov;
            var pakety = new List<byte[]>();
            if (!rozpracovane.TryGetValue(seriove, out var paket) || (data[i + 5] & 1) == 0)
            {
                paket = [];
            }

            for (var k = 0; k < segmentov; k++)
            {
                var dlzka = data[i + 27 + k];
                if (obsah + dlzka > data.Length)
                {
                    break;
                }

                paket.AddRange(data.AsSpan(obsah, dlzka));
                obsah += dlzka;
                if (dlzka < 255)
                {
                    pakety.Add([.. paket]);
                    paket = [];
                }
            }

            rozpracovane[seriove] = paket;
            stranky.Add((seriove, pozicia, pakety));
            i = obsah;
        }

        return stranky;
    }

    private static byte[] Precitaj(Stream subor, long pozicia, int dlzka)
    {
        var data = new byte[(int)Math.Max(0, Math.Min(dlzka, subor.Length - pozicia))];
        subor.Position = pozicia;
        subor.ReadExactly(data);
        return data;
    }
}
