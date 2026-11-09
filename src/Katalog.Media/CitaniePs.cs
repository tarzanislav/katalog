using Katalog.Jadro.Model;

namespace Katalog.Media;

/// <summary>
/// KATALOG's own reader of MPEG program streams: VOB of DVDs and MPG/MPEG files (MPEG-1 and MPEG-2). The start of the
/// file gives the streams and their first frames (video E0, MPEG audio C0–DF, and in private stream 1 the AC-3, DTS,
/// LPCM and subpicture substreams of a DVD), the middle and the end the length and the share of each stream.
/// </summary>
public static class CitaniePs
{
    private const int Zaciatok = 4 << 20;
    private const int Okno = 1 << 20;
    private const int NajviacVideo = 512 << 10;
    private const int NajviacZvuk = 64 << 10;

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

    private sealed class Tok(int id, InfoStopy stopa)
    {
        public int Id { get; } = id;

        public InfoStopy Stopa { get; } = stopa;

        public List<byte> Data { get; } = [];

        public long Bajtov { get; set; }

        public long? PrvyPts { get; set; }

        public long? PoslednyPts { get; set; }
    }

    private static InfoSuboru? CitajBezpecne(Stream subor)
    {
        var data = Precitaj(subor, 0, Zaciatok);
        if (data.Length < 14 || data[0] != 0 || data[1] != 0 || data[2] != 1 || data[3] != 0xBA)
        {
            return null;
        }

        var mpeg2 = (data[4] & 0xC0) == 0x40;
        var toky = new Dictionary<int, Tok>();
        Spracuj(data, toky, prvy: true);
        if (toky.Count == 0)
        {
            return null;
        }

        if (subor.Length > Zaciatok + (2L * Okno))
        {
            Spracuj(Precitaj(subor, subor.Length / 2, Okno), toky, prvy: false);
            Spracuj(Precitaj(subor, subor.Length - Okno, Okno), toky, prvy: false);
        }

        var info = new InfoSuboru { Velkost = subor.Length, Format = "MPEG-PS", VerziaFormatu = mpeg2 ? "MPEG-2 (DVD-Video, VOB)" : "MPEG-1" };
        var video = toky.Values.FirstOrDefault(t => t.Stopa.JeVideo && t.PrvyPts is not null && t.PoslednyPts is not null)
                    ?? toky.Values.FirstOrDefault(t => t.PrvyPts is not null && t.PoslednyPts is not null);
        if (video is not null && video.PoslednyPts > video.PrvyPts)
        {
            info.DlzkaSekund = (video.PoslednyPts!.Value - video.PrvyPts!.Value) / 90000.0;
        }

        var vsetkych = toky.Values.Sum(t => t.Bajtov);
        var poradie = 0;
        foreach (var tok in toky.Values.OrderBy(t => t.Stopa.Druh).ThenBy(t => t.Id))
        {
            var s = tok.Stopa;
            s.Id = ++poradie;
            s.IdToku = tok.Id;
            if (tok.Data.Count > 0)
            {
                ElementarneToky.Analyzuj(tok.Data.ToArray(), s);
            }

            s.DlzkaSekund ??= info.DlzkaSekund;
            if (vsetkych > 0)
            {
                s.VelkostStreamu = (long)(tok.Bajtov / (double)vsetkych * info.Velkost * 0.97);
                s.Odhad = true;
                if (s.DatovyTok is null && info.DlzkaSekund is > 0)
                {
                    s.DatovyTok = (long)(s.VelkostStreamu * 8 / info.DlzkaSekund.Value);
                    s.OdhadToku = true;
                }
            }

            info.Stopy.Add(s);
        }

        // the time stamps mark the starts of frames: the last frame lasts one more frame
        if (info.DlzkaSekund is { } dlzka && info.Stopy.FirstOrDefault(t => t.JeVideo)?.Snimky is > 0 and var snimky)
        {
            info.DlzkaSekund = dlzka + (1 / snimky);
            foreach (var t in info.Stopy.Where(t => t.DlzkaSekund == dlzka))
            {
                t.DlzkaSekund = info.DlzkaSekund;
            }
        }

        return info;
    }

    private static void Spracuj(ReadOnlySpan<byte> data, Dictionary<int, Tok> toky, bool prvy)
    {
        var i = 0;
        while (i + 6 <= data.Length)
        {
            if (data[i] != 0 || data[i + 1] != 0 || data[i + 2] != 1)
            {
                var dalsi = data[(i + 1)..].IndexOf([(byte)0, (byte)0, (byte)1]);
                if (dalsi < 0)
                {
                    return;
                }

                i += dalsi + 1;
                continue;
            }

            var id = data[i + 3];
            if (id == 0xBA)
            {
                // pack header: MPEG-2 has 14 bytes and stuffing, MPEG-1 12
                i += (data[i + 4] & 0xC0) == 0x40 && i + 14 <= data.Length ? 14 + (data[i + 13] & 7) : 12;
                continue;
            }

            if (id < 0xB9)
            {
                i += 4;
                continue;
            }

            var dlzka = (data[i + 4] << 8) | data[i + 5];
            var koniec = Math.Min(data.Length, i + 6 + dlzka);
            if (id is 0xBD or (>= 0xC0 and <= 0xDF) or (>= 0xE0 and <= 0xEF))
            {
                Pes(data[i..koniec], id, toky, prvy);
            }

            i = koniec;
        }
    }

    private static void Pes(ReadOnlySpan<byte> pes, int id, Dictionary<int, Tok> toky, bool prvy)
    {
        if (pes.Length < 9)
        {
            return;
        }

        int obsah;
        long? pts = null;
        if ((pes[6] & 0xC0) == 0x80)
        {
            // MPEG-2 PES header
            obsah = 9 + pes[8];
            if ((pes[7] & 0x80) != 0 && pes.Length >= 14)
            {
                pts = (((long)(pes[9] >> 1) & 7) << 30) | ((long)pes[10] << 22) | (((long)pes[11] >> 1) << 15) | ((long)pes[12] << 7) | ((long)pes[13] >> 1);
            }
        }
        else
        {
            // MPEG-1: stuffing, buffer size, time stamps
            obsah = 6;
            while (obsah < pes.Length && pes[obsah] == 0xFF)
            {
                obsah++;
            }

            if (obsah < pes.Length && (pes[obsah] & 0xC0) == 0x40)
            {
                obsah += 2;
            }

            if (obsah + 5 <= pes.Length && (pes[obsah] & 0xE0) == 0x20)
            {
                pts = (((long)(pes[obsah] >> 1) & 7) << 30) | ((long)pes[obsah + 1] << 22) | (((long)pes[obsah + 2] >> 1) << 15) |
                      ((long)pes[obsah + 3] << 7) | ((long)pes[obsah + 4] >> 1);
                obsah += (pes[obsah] & 0xF0) == 0x30 ? 10 : 5;
            }
            else if (obsah < pes.Length)
            {
                obsah++;
            }
        }

        if (obsah >= pes.Length)
        {
            return;
        }

        var payload = pes[obsah..];
        var kluc = id;
        if (id == 0xBD)
        {
            // private stream 1 of a DVD: the substream number, and for audio 3 more bytes (frames, first access unit)
            var podtok = payload[0];
            kluc = 0x100 | podtok;
            payload = podtok switch
            {
                >= 0x80 and <= 0x8F when payload.Length > 4 => payload[4..],
                >= 0xA0 and <= 0xAF when payload.Length > 7 => payload[1..],
                >= 0x20 and <= 0x3F => payload[1..],
                _ => payload[1..],
            };
        }

        if (!toky.TryGetValue(kluc, out var tok))
        {
            if (!prvy || Druh(id, kluc) is not { } stopa)
            {
                return;
            }

            tok = new Tok(kluc, stopa);
            toky[kluc] = tok;
        }

        tok.Bajtov += pes.Length;
        if (pts is { } p)
        {
            tok.PrvyPts ??= p;
            tok.PoslednyPts = p;
        }

        var najviac = tok.Stopa.JeVideo ? NajviacVideo : NajviacZvuk;
        if (prvy && tok.Data.Count < najviac)
        {
            tok.Data.AddRange(payload[..Math.Min(payload.Length, najviac - tok.Data.Count)]);
        }
    }

    private static InfoStopy? Druh(int id, int kluc)
    {
        (DruhStopy? Druh, string? Kodek, string? Format, string? FormatInfo, string? Obchodny) t = kluc switch
        {
            >= 0xE0 and <= 0xEF => (DruhStopy.Video, "mpeg2video", "MPEG Video", (string?)null, (string?)null),
            >= 0xC0 and <= 0xDF => (DruhStopy.Zvuk, "mp2", "MPEG Audio", null, null),
            >= 0x180 and <= 0x187 => (DruhStopy.Zvuk, "ac3", "AC-3", "Audio Coding 3", "Dolby Digital"),
            >= 0x188 and <= 0x18F => (DruhStopy.Zvuk, "dts", "DTS", "Digital Theater Systems", "DTS"),
            >= 0x1A0 and <= 0x1AF => (DruhStopy.Zvuk, "pcm_dvd", "PCM", "DVD LPCM", null),
            >= 0x120 and <= 0x13F => (DruhStopy.Titulky, "dvd_subtitle", "RLE", "Subpicture DVD", null),
            _ => ((DruhStopy?)null, null, null, null, null),
        };
        var (druh, kodek, format, formatInfo, obchodny) = t;
        return druh is null
            ? null
            : new InfoStopy
            {
                Druh = druh,
                JeVideo = druh == DruhStopy.Video,
                Kodek = kodek,
                Format = format,
                FormatInfo = formatInfo,
                ObchodnyNazov = obchodny,
                IdKodeku = "0x" + (kluc > 0xFF ? $"BD-{kluc & 0xFF:X2}" : id.ToString("X2", System.Globalization.CultureInfo.InvariantCulture)),
            };
    }

    private static byte[] Precitaj(Stream subor, long pozicia, int dlzka)
    {
        var data = new byte[(int)Math.Max(0, Math.Min(dlzka, subor.Length - pozicia))];
        subor.Position = pozicia;
        subor.ReadExactly(data);
        return data;
    }
}
