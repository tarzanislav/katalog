using System.Buffers.Binary;
using System.Text;
using Katalog.Jadro.Model;

namespace Katalog.Media;

/// <summary>
/// KATALOG's own reader of MPEG transport streams: TS (DVB, 188-byte packets), M2TS/MTS (Blu-ray and AVCHD, 192 bytes)
/// and 204-byte packets. Only three windows of the file are read – the start (tables PAT and PMT, the first frames of
/// each stream), the middle and the end (time stamps for the length, packet counts for the share of each stream).
/// Languages come from the PMT, and for Blu-ray from the clip information file (BDMV/CLIPINF/NNNNN.clpi) next to the
/// stream. A file it cannot follow gives null.
/// </summary>
public static class CitanieTs
{
    private const int Zaciatok = 4 << 20;
    private const int Okno = 1 << 20;

    /// <summary>The first bytes kept of each stream: enough for a key picture's sequence header, a few audio frames.</summary>
    private const int NajviacVideo = 1 << 20;
    private const int NajviacZvuk = 96 << 10;

    public static InfoSuboru? Citaj(string cesta)
    {
        try
        {
            using var subor = new FileStream(cesta, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 1 << 16, FileOptions.RandomAccess);
            var info = Citaj(subor);
            if (info is not null)
            {
                JazykyBluray(cesta, info);
            }

            return info;
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

    /// <summary>The packet size and the offset of the sync byte in it (188/0, 192/4, 204/0); null when not a transport stream.</summary>
    internal static (int Velkost, int Posun)? Paket(ReadOnlySpan<byte> data)
    {
        foreach (var (velkost, posun) in new[] { (192, 4), (188, 0), (204, 0) })
        {
            var ok = data.Length >= posun + (velkost * 5);
            for (var k = 0; ok && k < 5; k++)
            {
                ok = data[posun + (k * velkost)] == 0x47;
            }

            if (ok)
            {
                return (velkost, posun);
            }
        }

        return null;
    }

    private static InfoSuboru? CitajBezpecne(Stream subor)
    {
        var zaciatok = Precitaj(subor, 0, Zaciatok);
        if (Paket(zaciatok) is not { } paket)
        {
            return null;
        }

        var stav = new Stav(paket.Velkost, paket.Posun);
        stav.Spracuj(zaciatok, prvy: true);
        if (stav.Toky.Count == 0)
        {
            return null;
        }

        // the middle and the end: the share of each stream, the last time stamps
        if (subor.Length > Zaciatok + (2L * Okno))
        {
            stav.Spracuj(Precitaj(subor, ZarovnajNaPaket(subor.Length / 2, paket.Velkost), Okno), prvy: false);
            stav.Spracuj(Precitaj(subor, ZarovnajNaPaket(subor.Length - Okno, paket.Velkost), Okno), prvy: false);
        }

        var info = new InfoSuboru
        {
            Velkost = subor.Length,
            Format = paket.Velkost == 192 ? "BDAV (M2TS)" : "MPEG-TS",
            VerziaFormatu = paket.Velkost == 192 ? "192-bajtové pakety (Blu-ray, AVCHD)" : $"{paket.Velkost}-bajtové pakety",
        };
        info.DlzkaSekund = stav.Dlzka();
        var vsetkych = stav.Toky.Values.Sum(t => t.Paketov);
        var poradie = 0;
        foreach (var tok in stav.Toky.Values.OrderBy(t => t.Pid))
        {
            var s = tok.Stopa;
            s.Id = ++poradie;
            s.IdToku = tok.Pid;
            if (tok.Data.Count > 0)
            {
                ElementarneToky.Analyzuj(tok.Data.ToArray(), s);
            }

            if (s.Druh is null)
            {
                continue;
            }

            s.DlzkaSekund ??= info.DlzkaSekund;
            if (vsetkych > 0 && tok.Paketov > 0)
            {
                var podiel = tok.Paketov / (double)vsetkych;
                s.VelkostStreamu = (long)(podiel * info.Velkost * (paket.Velkost - 4.0 - paket.Posun) / paket.Velkost);
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

        return info.Stopy.Count == 0 ? null : info;
    }

    private static long ZarovnajNaPaket(long pozicia, int velkost) => pozicia - (pozicia % velkost);

    private static byte[] Precitaj(Stream subor, long pozicia, int dlzka)
    {
        var data = new byte[(int)Math.Max(0, Math.Min(dlzka, subor.Length - pozicia))];
        subor.Position = pozicia;
        subor.ReadExactly(data);
        return data;
    }

    // ---------------------------------------------------------------- the streams of a transport stream

    private sealed class Tok(int pid, InfoStopy stopa)
    {
        public int Pid { get; } = pid;

        public InfoStopy Stopa { get; } = stopa;

        public List<byte> Data { get; } = [];

        public long Paketov { get; set; }

        public long? PrvyPts { get; set; }

        public long? PoslednyPts { get; set; }

        public bool Zacaty { get; set; }
    }

    private sealed class Stav(int velkostPaketu, int posun)
    {
        private readonly HashSet<int> _pmt = [];
        private readonly Dictionary<int, List<byte>> _sekcie = [];
        private int? _pcrPid;
        private long? _prvyPcr;
        private long? _poslednyPcr;

        public Dictionary<int, Tok> Toky { get; } = [];

        public void Spracuj(ReadOnlySpan<byte> data, bool prvy)
        {
            for (var p = posun; p + 188 <= data.Length; p += velkostPaketu)
            {
                if (data[p] != 0x47)
                {
                    // lost sync: find the next packet
                    var dalsi = data[(p + 1)..].IndexOf((byte)0x47);
                    if (dalsi < 0)
                    {
                        return;
                    }

                    p += dalsi + 1 - velkostPaketu;
                    continue;
                }

                var paket = data.Slice(p, 188);
                var zaciatokJednotky = (paket[1] & 0x40) != 0;
                var pid = ((paket[1] & 0x1F) << 8) | paket[2];
                var adaptacia = (paket[3] >> 4) & 3;
                var obsah = 4;
                if (adaptacia is 2 or 3)
                {
                    var dlzka = paket[4];
                    if (dlzka > 0 && (paket[5] & 0x10) != 0 && pid == _pcrPid && dlzka >= 7)
                    {
                        var pcr = ((long)paket[6] << 25) | ((long)paket[7] << 17) | ((long)paket[8] << 9) | ((long)paket[9] << 1) | ((long)paket[10] >> 7);
                        _prvyPcr ??= pcr;
                        _poslednyPcr = pcr;
                    }

                    obsah = 5 + dlzka;
                }

                if (adaptacia is 0 or 2 || obsah >= 188)
                {
                    continue;
                }

                var payload = paket[obsah..];
                if (pid == 0 || _pmt.Contains(pid))
                {
                    if (prvy)
                    {
                        Sekcia(pid, payload, zaciatokJednotky);
                    }

                    continue;
                }

                if (!Toky.TryGetValue(pid, out var tok))
                {
                    continue;
                }

                tok.Paketov++;
                if (zaciatokJednotky && Pts(payload) is { } pts)
                {
                    tok.PrvyPts ??= pts;
                    tok.PoslednyPts = pts;
                }

                if (!prvy)
                {
                    continue;
                }

                var najviac = tok.Stopa.Druh == DruhStopy.Video ? NajviacVideo : NajviacZvuk;
                if (zaciatokJednotky)
                {
                    tok.Zacaty = true;
                    var hlavicka = payload.Length > 9 && payload[0] == 0 && payload[1] == 0 && payload[2] == 1 ? 9 + payload[8] : 0;
                    payload = hlavicka < payload.Length ? payload[hlavicka..] : [];
                }

                if (tok.Zacaty && tok.Data.Count < najviac)
                {
                    tok.Data.AddRange(payload[..Math.Min(payload.Length, najviac - tok.Data.Count)]);
                }
            }
        }

        /// <summary>The length from the time stamps of the first video (or any) stream, or from the PCR.</summary>
        public double? Dlzka()
        {
            var tok = Toky.Values.Where(t => t.PrvyPts is not null && t.PoslednyPts is not null)
                .OrderByDescending(t => t.Stopa.Druh == DruhStopy.Video).ThenByDescending(t => t.Paketov).FirstOrDefault();
            if (tok is not null && Rozdiel(tok.PrvyPts!.Value, tok.PoslednyPts!.Value) is > 0 and var d)
            {
                return d / 90000.0;
            }

            return _prvyPcr is { } a && _poslednyPcr is { } b && Rozdiel(a, b) is > 0 and var e ? e / 90000.0 : null;
        }

        /// <summary>The difference of two 33-bit time stamps (a stream may wrap around).</summary>
        private static long Rozdiel(long a, long b) => b >= a ? b - a : b + (1L << 33) - a;

        private static long? Pts(ReadOnlySpan<byte> pes)
        {
            if (pes.Length < 14 || pes[0] != 0 || pes[1] != 0 || pes[2] != 1 || (pes[7] & 0x80) == 0)
            {
                return null;
            }

            return (((long)(pes[9] >> 1) & 7) << 30) | ((long)pes[10] << 22) | (((long)pes[11] >> 1) << 15) | ((long)pes[12] << 7) | ((long)pes[13] >> 1);
        }

        private void Sekcia(int pid, ReadOnlySpan<byte> payload, bool zaciatok)
        {
            if (zaciatok)
            {
                var ukazovatel = payload[0];
                if (ukazovatel + 1 >= payload.Length)
                {
                    return;
                }

                _sekcie[pid] = [.. payload[(1 + ukazovatel)..]];
            }
            else if (_sekcie.TryGetValue(pid, out var rozpracovana))
            {
                rozpracovana.AddRange(payload);
            }
            else
            {
                return;
            }

            var sekcia = _sekcie[pid];
            if (sekcia.Count < 3)
            {
                return;
            }

            var dlzka = ((sekcia[1] & 0x0F) << 8) | sekcia[2];
            if (sekcia.Count < 3 + dlzka)
            {
                return;
            }

            var tabulka = sekcia.GetRange(0, 3 + dlzka).ToArray();
            _sekcie.Remove(pid);
            if (pid == 0 && tabulka[0] == 0x00)
            {
                Pat(tabulka);
            }
            else if (tabulka[0] == 0x02)
            {
                Pmt(tabulka);
            }
        }

        private void Pat(byte[] t)
        {
            for (var i = 8; i + 4 <= t.Length - 4; i += 4)
            {
                var program = (t[i] << 8) | t[i + 1];
                var pid = ((t[i + 2] & 0x1F) << 8) | t[i + 3];
                if (program != 0)
                {
                    _pmt.Add(pid);
                }
            }
        }

        private void Pmt(byte[] t)
        {
            if (t.Length < 16)
            {
                return;
            }

            _pcrPid ??= ((t[8] & 0x1F) << 8) | t[9];
            var dlzkaInfo = ((t[10] & 0x0F) << 8) | t[11];
            var i = 12 + dlzkaInfo;
            var programDolbyVision = Deskriptory(t.AsSpan(12, Math.Min(dlzkaInfo, t.Length - 12))).Any(d => d.Tag == 0xB0);
            while (i + 5 <= t.Length - 4)
            {
                var typ = t[i];
                var pid = ((t[i + 1] & 0x1F) << 8) | t[i + 2];
                var dlzka = ((t[i + 3] & 0x0F) << 8) | t[i + 4];
                var deskriptory = Deskriptory(t.AsSpan(i + 5, Math.Min(dlzka, Math.Max(0, t.Length - 4 - i - 5))));
                i += 5 + dlzka;
                if (Toky.ContainsKey(pid))
                {
                    continue;
                }

                var stopa = Druh(typ, deskriptory);
                if (stopa is null)
                {
                    continue;
                }

                if (deskriptory.Any(d => d.Tag == 0xB0) || programDolbyVision && typ == 0x06 && deskriptory.Any(d => d.Tag == 0xB0))
                {
                    stopa.Hdr = Hdr.DolbyVision;
                }

                Toky[pid] = new Tok(pid, stopa);
            }

            // a Dolby Vision enhancement layer (its own PID with a DOVI descriptor) marks the base layer
            var dv = Toky.Values.FirstOrDefault(v => v.Stopa.Hdr == Hdr.DolbyVision && v.Stopa.Druh is null);
            if (dv is not null && Toky.Values.FirstOrDefault(v => v.Stopa.JeVideo) is { } zakladna)
            {
                zakladna.Stopa.Hdr = Hdr.DolbyVision;
                zakladna.Stopa.HdrPopis ??= "Dolby Vision (vrstva rozšírenia)";
            }
        }

        private static List<(byte Tag, byte[] Data)> Deskriptory(ReadOnlySpan<byte> data)
        {
            var zoznam = new List<(byte, byte[])>();
            var i = 0;
            while (i + 2 <= data.Length)
            {
                var dlzka = data[i + 1];
                if (i + 2 + dlzka > data.Length)
                {
                    break;
                }

                zoznam.Add((data[i], data.Slice(i + 2, dlzka).ToArray()));
                i += 2 + dlzka;
            }

            return zoznam;
        }

        /// <summary>A stream of the PMT by its stream type and descriptors (registration, ISO 639 language, DVB …).</summary>
        private static InfoStopy? Druh(byte typ, List<(byte Tag, byte[] Data)> d)
        {
            var registracia = d.Where(x => x.Tag == 0x05 && x.Data.Length >= 4).Select(x => Encoding.ASCII.GetString(x.Data, 0, 4)).FirstOrDefault();
            (DruhStopy? Druh, string? Kodek, string? Format, string? FormatInfo, string? Obchodny) t = typ switch
            {
                0x01 => (DruhStopy.Video, "mpeg1video", "MPEG Video", "MPEG-1", (string?)null),
                0x02 => (DruhStopy.Video, "mpeg2video", "MPEG Video", "MPEG-2", null),
                0x10 => (DruhStopy.Video, "mpeg4", "MPEG-4 Visual", null, null),
                0x1B => (DruhStopy.Video, "h264", "AVC", "Advanced Video Codec", null),
                0x20 => (DruhStopy.Video, "h264", "AVC", "MVC (3D)", null),
                0x24 => (DruhStopy.Video, "hevc", "HEVC", "High Efficiency Video Coding", null),
                0x33 => (DruhStopy.Video, "vvc", "VVC", "Versatile Video Coding", null),
                0xEA => (DruhStopy.Video, "vc1", "VC-1", null, null),
                0x03 => (DruhStopy.Zvuk, "mp2", "MPEG Audio", null, null),
                0x04 => (DruhStopy.Zvuk, "mp2", "MPEG Audio", null, null),
                0x0F => (DruhStopy.Zvuk, "aac", "AAC", "Advanced Audio Codec", null),
                0x11 => (DruhStopy.Zvuk, "aac", "AAC", "LATM", null),
                0x80 => (DruhStopy.Zvuk, "pcm_bluray", "PCM", "Blu-ray LPCM", null),
                0x81 => (DruhStopy.Zvuk, "ac3", "AC-3", "Audio Coding 3", "Dolby Digital"),
                0x82 or 0x85 or 0x86 or 0xA2 => (DruhStopy.Zvuk, "dts", "DTS", "Digital Theater Systems", "DTS"),
                0x83 => (DruhStopy.Zvuk, "truehd", "MLP FBA", "Meridian Lossless Packing FBA", "Dolby TrueHD"),
                0x84 or 0x87 or 0xA1 => (DruhStopy.Zvuk, "eac3", "E-AC-3", "Enhanced AC-3", "Dolby Digital Plus"),
                0x90 => (DruhStopy.Titulky, "hdmv_pgs_subtitle", "PGS", "Presentation Graphic Stream", null),
                0x92 => (DruhStopy.Titulky, "hdmv_text_subtitle", "TextST", null, null),
                0x06 when d.Any(x => x.Tag == 0x6A) || registracia == "AC-3" => (DruhStopy.Zvuk, "ac3", "AC-3", "Audio Coding 3", "Dolby Digital"),
                0x06 when d.Any(x => x.Tag == 0x7A) || registracia == "EAC3" => (DruhStopy.Zvuk, "eac3", "E-AC-3", "Enhanced AC-3", "Dolby Digital Plus"),
                0x06 when d.Any(x => x.Tag == 0x7B) || registracia is "DTS1" or "DTS2" or "DTS3" => (DruhStopy.Zvuk, "dts", "DTS", "Digital Theater Systems", "DTS"),
                0x06 when registracia == "Opus" => (DruhStopy.Zvuk, "opus", "Opus", null, null),
                0x06 when registracia == "AC-4" || d.Any(x => x.Tag == 0x7F && x.Data.Length > 0 && x.Data[0] == 0x15) => (DruhStopy.Zvuk, "ac4", "AC-4", "Audio Coding 4", "Dolby AC-4"),
                0x06 when d.Any(x => x.Tag == 0x59) => (DruhStopy.Titulky, "dvb_subtitle", "DVB Subtitle", null, null),
                0x06 when d.Any(x => x.Tag == 0x56) => (DruhStopy.Titulky, "dvb_teletext", "Teletext", null, null),
                0x06 when d.Any(x => x.Tag == 0xB0) => ((DruhStopy?)null, "dovi", "Dolby Vision", null, null),
                _ => ((DruhStopy?)null, null, null, null, null),
            };
        var (druh, kodek, format, formatInfo, obchodny) = t;
            if (kodek is null)
            {
                return null;
            }

            var stopa = new InfoStopy
            {
                Druh = druh,
                JeVideo = druh == DruhStopy.Video,
                Kodek = kodek,
                Format = format,
                FormatInfo = formatInfo,
                ObchodnyNazov = obchodny,
                IdKodeku = "0x" + typ.ToString("X2", System.Globalization.CultureInfo.InvariantCulture),
                Predvolena = false,
            };

            // ISO 639 language (0x0A), DVB subtitling (0x59) and teletext (0x56) carry a language too
            var jazyk = d.Where(x => x.Tag is 0x0A or 0x59 or 0x56 && x.Data.Length >= 3).Select(x => Encoding.Latin1.GetString(x.Data, 0, 3)).FirstOrDefault();
            if (jazyk is not null)
            {
                stopa.Jazyk = Jazyky.Normalizuj(jazyk);
            }

            if (d.FirstOrDefault(x => x.Tag == 0x59 && x.Data.Length >= 4).Data is { } dvb && dvb[3] is 0x20 or 0x21 or 0x22 or 0x23 or 0x24 or 0x25)
            {
                stopa.Sdh = true; // subtitling type: for the hard of hearing
            }

            if (d.FirstOrDefault(x => x.Tag == 0x0A && x.Data.Length >= 4).Data is { } iso && iso[3] == 2)
            {
                stopa.Sdh = true; // audio type: hearing impaired
            }

            return stopa;
        }
    }

    // ---------------------------------------------------------------- Blu-ray clip information

    /// <summary>
    /// The languages of a Blu-ray stream (BDMV/STREAM/00001.m2ts) from its clip information (BDMV/CLIPINF/00001.clpi):
    /// the PIDs of audio, PG and text subtitle streams with their ISO 639-2 codes.
    /// </summary>
    internal static void JazykyBluray(string cesta, InfoSuboru info)
    {
        var priecinok = Path.GetDirectoryName(cesta);
        if (priecinok is null || !string.Equals(Path.GetFileName(priecinok), "STREAM", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var clpi = Path.Combine(Path.GetDirectoryName(priecinok) ?? "", "CLIPINF", Path.GetFileNameWithoutExtension(cesta) + ".clpi");
        if (!File.Exists(clpi) || new FileInfo(clpi).Length > (4 << 20))
        {
            return;
        }

        foreach (var (pid, jazyk) in JazykyClpi(File.ReadAllBytes(clpi)))
        {
            if (info.Stopy.FirstOrDefault(s => s.IdToku == pid) is { } stopa && Jazyky.Normalizuj(jazyk) is { } kod)
            {
                stopa.Jazyk = kod;
            }
        }
    }

    internal static IEnumerable<(int Pid, string Jazyk)> JazykyClpi(byte[] c)
    {
        var vysledok = new List<(int, string)>();
        if (c.Length < 40 || Encoding.ASCII.GetString(c, 0, 4) != "HDMV")
        {
            return vysledok;
        }

        var program = (int)BinaryPrimitives.ReadUInt32BigEndian(c.AsSpan(12));
        if (program <= 0 || program + 6 > c.Length)
        {
            return vysledok;
        }

        var i = program + 4 + 1;
        var sekvencii = c[i++];
        for (var p = 0; p < sekvencii && i + 8 <= c.Length; p++)
        {
            i += 4 + 2; // SPN_program_sequence_start, program_map_PID
            var tokov = c[i];
            i += 2; // number of streams, number of groups
            for (var t = 0; t < tokov && i + 3 <= c.Length; t++)
            {
                var pid = BinaryPrimitives.ReadUInt16BigEndian(c.AsSpan(i));
                var dlzka = c[i + 2];
                var info = i + 3;
                if (info + dlzka > c.Length || dlzka < 1)
                {
                    return vysledok;
                }

                var typ = c[info];
                var jazyk = typ switch
                {
                    0x80 or 0x81 or 0x82 or 0x83 or 0x84 or 0x85 or 0x86 or 0xA1 or 0xA2 when dlzka >= 5 => Encoding.ASCII.GetString(c, info + 2, 3),
                    0x90 or 0x91 when dlzka >= 4 => Encoding.ASCII.GetString(c, info + 1, 3),
                    0x92 when dlzka >= 5 => Encoding.ASCII.GetString(c, info + 2, 3),
                    _ => null,
                };
                if (jazyk is not null && jazyk.All(char.IsAsciiLetter))
                {
                    vysledok.Add((pid, jazyk));
                }

                i = info + dlzka;
            }
        }

        return vysledok;
    }
}
