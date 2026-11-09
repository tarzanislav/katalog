using System.Buffers.Binary;
using System.Diagnostics;
using System.Text;
using Katalog.Jadro.Model;
using Katalog.Media;

namespace Katalog.Testy;

/// <summary>
/// The own reader of transport streams (TS, M2TS), program streams (VOB, MPG) and AVI (owner, 9. 11. 2026): streams put
/// together here packet by packet with invented content, and – where ffmpeg is installed – real files it writes.
/// </summary>
public sealed class VlastnaCitackaTokyTesty
{
    /// <summary>An MPEG-2 sequence header 1280×720, 16:9, 50 fps, 15 Mb/s, with its extension Main@High, progressive.</summary>
    private static readonly byte[] Mpeg2 =
    [
        0, 0, 1, 0xB3, 0x50, 0x02, 0xD0, 0x36, 0x24, 0x9F, 0x20, 0x00,
        0, 0, 1, 0xB5, 0x14, 0x4A, 0x00, 0x01, 0x00, 0x00,
    ];

    /// <summary>An AC-3 frame header: 48 kHz, 384 kb/s, 3/2 with LFE, dialnorm 27.</summary>
    private static readonly byte[] Ac3 = [0x0B, 0x77, 0, 0, 0x1C, 0x40, 0xE1, 0xD8, 0, 0, 0, 0];

    [Test]
    public void TransportnyTokAkoBluray()
    {
        var ts = Ts(192, Pmt(0x1011, (0x02, 0x1011, []), (0x81, 0x1100, [0x0A, 4, (byte)'c', (byte)'e', (byte)'s', 0]), (0x90, 0x1200, [])),
            (0x1011, Pes(0xE0, 0, Mpeg2)), (0x1100, Pes(0xBD, 0, Ac3)), (0x1011, Pes(0xE0, 90000 * 10, [0, 0, 1, 0])),
            (0x1100, Pes(0xBD, 90000 * 10, Ac3)));
        var info = CitanieTs.Citaj(new MemoryStream(ts))!;
        Over.Pravda(info is { Format: "BDAV (M2TS)" }, "192-bajtové pakety sú M2TS");
        Over.Rovne(10.02, Math.Round(info.DlzkaSekund!.Value, 2), "dĺžka z PTS a jedna snímka navyše");
        var video = info.Stopy.Single(s => s.JeVideo);
        Over.Pravda(video is { Kodek: "mpeg2video", Sirka: 1280, Vyska: 720, Snimky: 50, Profil: "Main@High", Prekladane: false, MaxTok: 15_000_000, IdToku: 0x1011 },
            "MPEG-2 zo sekvenčnej hlavičky");
        Over.Rovne(16 / 9.0, video.PomerStran, "16:9");
        var zvuk = info.Stopy.Single(s => s.Druh == DruhStopy.Zvuk);
        Over.Pravda(zvuk is { Kodek: "ac3", Jazyk: "cs", Kanaly: 6, Vzorkovanie: 48000, DatovyTok: 384_000, Dialogy: -27, RozlozenieKanalov: "L R C LFE Ls Rs" },
            $"AC-3 z rámca, jazyk z PMT ({zvuk.Kodek} {zvuk.Jazyk} {zvuk.Kanaly} {zvuk.Vzorkovanie} {zvuk.DatovyTok} {zvuk.Dialogy} {zvuk.RozlozenieKanalov})");
        Over.Pravda(info.Stopy.Single(s => s.Druh == DruhStopy.Titulky) is { Kodek: "hdmv_pgs_subtitle" }, "PGS titulky");

        // the clip information of a Blu-ray gives the languages
        var clpi = Clpi((0x1100, 0x81, "slk"), (0x1200, 0x90, "eng"));
        var jazyky = CitanieTs.JazykyClpi(clpi).ToList();
        Over.Postupnost(new[] { (0x1100, "slk"), (0x1200, "eng") }, jazyky.Select(j => (j.Pid, j.Jazyk)), "jazyky z CLIPINF");
        using var priecinok = new DocasnyPriecinok();
        var stream = Directory.CreateDirectory(Path.Combine(priecinok.Cesta, "BDMV", "STREAM")).FullName;
        Directory.CreateDirectory(Path.Combine(priecinok.Cesta, "BDMV", "CLIPINF"));
        File.WriteAllBytes(Path.Combine(stream, "00001.m2ts"), ts);
        File.WriteAllBytes(Path.Combine(priecinok.Cesta, "BDMV", "CLIPINF", "00001.clpi"), clpi);
        var bluray = CitanieKontajnera.Citaj(Path.Combine(stream, "00001.m2ts"))!;
        Over.Postupnost(["sk", "en"], bluray.Stopy.Select(s => s.Jazyk ?? "?"), "jazyky Blu-ray v stopách knižnice");
        Over.Pravda(bluray.Udaje is { Kontajner: "m2ts", VideoKodek: "mpeg2video", Sirka: 1280 }, "údaje knižnice");
    }

    [Test]
    public void ProgramovyTokAkoDvd()
    {
        byte[] lpcm = [0x01, 0x00, 0x04, 0x00, 0x51, 0x80, 0x00, 0x00];
        var ps = Ps(Pes(0xE0, 0, [.. Mpeg2[..12], 0, 0, 1, 0xB5, 0x14, 0x82, 0x00, 0x01, 0x00, 0x00]), Pes(0xBD, 0, [0x80, 1, 0, 1, .. Ac3]),
            Pes(0xBD, 0, [0xA0, .. lpcm]), Pes(0xBD, 0, [0x20, 1, 2, 3]), Pes(0xE0, 90000 * 4, [0, 0, 1, 0]));
        var info = CitaniePs.Citaj(new MemoryStream(ps))!;
        Over.Pravda(info is { Format: "MPEG-PS", VerziaFormatu: "MPEG-2 (DVD-Video, VOB)" }, "VOB");
        Over.Pravda(info.Stopy.Single(s => s.JeVideo) is { Kodek: "mpeg2video", Prekladane: true, Sirka: 1280 }, "MPEG-2 prekladané");
        Over.Pravda(info.Stopy.Single(s => s.Kodek == "ac3") is { Kanaly: 6, IdToku: 0x180 }, "AC-3 v súkromnom prúde 1");
        Over.Pravda(info.Stopy.Single(s => s.Kodek == "pcm_dvd") is { Kanaly: 2, BitovaHlbka: 20, Vzorkovanie: 96000 }, "DVD LPCM");
        Over.Pravda(info.Stopy.Single(s => s.Druh == DruhStopy.Titulky) is { Kodek: "dvd_subtitle" }, "titulky DVD");
        Over.Rovne(4.02, Math.Round(info.DlzkaSekund!.Value, 2), "dĺžka");
    }

    [Test]
    public void AviXvidAMp3()
    {
        var avi = Avi();
        var info = CitanieAvi.Citaj(new MemoryStream(avi))!;
        Over.Pravda(info is { Format: "AVI", Nazov: "Vymyslený film" }, "AVI s názvom");
        var video = info.Stopy[0];
        Over.Pravda(video is { Kodek: "mpeg4", Sirka: 720, Vyska: 576, Snimky: 25, Profil: "Advanced Simple@L5", FormatInfo: "Xvid (build 50)" }, "Xvid z VOL");
        Over.Rovne(1.7778, Math.Round(video.PomerStran!.Value, 4), "pomer pixelov 64:45 → 16:9");
        Over.Rovne(1000.0, video.DlzkaSekund, "dĺžka 25 000 snímok");
        Over.Rovne(300L, video.VelkostStreamu, "veľkosť videa z idx1");
        var zvuk = info.Stopy[1];
        Over.Pravda(zvuk is { Kodek: "mp3", Kanaly: 2, Vzorkovanie: 48000, DatovyTok: 192_000 }, "MP3");
        Over.Rovne(CitanieKontajnera.FormatSuboru.Avi, CitanieKontajnera.Format(avi), "formát podľa obsahu");
    }

    [Test]
    public void ZvukoveRamceAElementarneToky()
    {
        // DTS core: 5.1, 48 kHz, 1536 kb/s
        var dts = new InfoStopy { Kodek = "dts" };
        AnalyzaRamcov.Dts(new Bity().Z(0x7FFE8001, 32).Z(1, 1).Z(31, 5).Z(0, 1).Z(15, 7).Z(1000, 14).Z(9, 6).Z(13, 4).Z(24, 5).Z(0, 10).Z(2, 2).Z(0, 48).Bajty(), dts);
        Over.Pravda(dts is { Kanaly: 6, Vzorkovanie: 48000, DatovyTok: 1_536_000 }, "jadro DTS");

        // TrueHD 7.1: the 8-channel presentation L R, C, LFE, Ls Rs, Lb Rb (bits 0, 1, 2, 3, 6)
        var thd = new InfoStopy { Kodek = "truehd" };
        var major = new Bity().Z(0xF8726FBA, 32).Z(0, 4).Z(0, 4).Z(0, 2).Z(0, 2).Z(0b01111, 5).Z(0, 2).Z(0b1001111, 13).Z(0xB752, 16)
            .Z(0, 16).Z(0, 16).Z(0, 16).Z(3, 4).Z(0, 4).Z(0, 8).Bajty();
        AnalyzaRamcov.Zvuk([0, 0, 0, 0, .. major], thd);
        Over.Pravda(thd is { Kanaly: 8, Vzorkovanie: 48000, Atmos: false }, "TrueHD 7.1");

        var adts = new InfoStopy { Kodek = "aac" };
        ElementarneToky.Analyzuj([0xFF, 0xF1, 0x4C, 0x80, 0x2E, 0x7F, 0xFC], adts);
        Over.Pravda(adts is { Profil: "LC", Vzorkovanie: 48000, Kanaly: 2 }, "AAC ADTS");

        var mp2 = new InfoStopy { Kodek = "mp2" };
        ElementarneToky.Analyzuj([0xFF, 0xFD, 0xA4, 0xC4], mp2);
        Over.Pravda(mp2 is { Kodek: "mp2", Kanaly: 1, Vzorkovanie: 48000, DatovyTok: 192_000, FormatInfo: "MPEG-1 Layer 2" }, "MPEG audio");

        var lpcm = new InfoStopy { Kodek = "pcm_bluray" };
        ElementarneToky.Analyzuj([0x05, 0xA0, 0x91, 0xC0], lpcm);
        Over.Pravda(lpcm is { Kanaly: 6, Vzorkovanie: 48000, BitovaHlbka: 24 }, "Blu-ray LPCM 5.1 24 bitov");

        byte[] asf = [0x30, 0x26, 0xB2, 0x75, 0x8E, 0x66, 0xCF, 0x11, 0xA6, 0xD9, 0x00, 0xAA, 0x00, 0x62, 0xCE, 0x6C];
        Over.Rovne(CitanieKontajnera.FormatSuboru.Neznamy, CitanieKontajnera.Format(asf), "ASF/WMV nepozná");
        Over.Pravda(Katalog.Data.TechnickeUdaje.VlastnaCitacka.ToHashSet(StringComparer.OrdinalIgnoreCase).SetEquals(CitanieKontajnera.Pripony),
            "knižnica a čítačka poznajú tie isté prípony");
    }

    /// <summary>Real files written by ffmpeg (GitHub Actions installs it on Linux), each checked against what it was told to write.</summary>
    [Test]
    public void SkutocneSuboryZFfmpeg()
    {
        var nastroje = HladanieFfmpeg.Najdi(Environment.GetEnvironmentVariable("KATALOG_FFMPEG_DIR"), AppContext.BaseDirectory);
        if (nastroje?.Ffmpeg is null)
        {
            Console.WriteLine("ffmpeg tu nie je – test vynechaný");
            return;
        }

        using var priecinok = new DocasnyPriecinok();
        string Vyrob(string meno, string parametre)
        {
            var cesta = Path.Combine(priecinok.Cesta, meno);
            using var proces = Process.Start(new ProcessStartInfo(nastroje.Ffmpeg, $"-v error -y {parametre} \"{cesta}\"")
            {
                RedirectStandardError = true,
                UseShellExecute = false,
            })!;
            proces.StandardError.ReadToEnd();
            proces.WaitForExit();
            return cesta;
        }

        const string Obraz = "-f lavfi -i testsrc2=size=1280x720:rate=25 -t 6";
        const string Zvuk = "-f lavfi -i sine=frequency=400:sample_rate=48000 -t 6";
        var ts = Vyrob("vymyslený.ts", $"{Obraz} {Zvuk} -map 0 -map 1 -c:v mpeg2video -b:v 4M -c:a ac3 -ac 6 -metadata:s:a:0 language=slk");
        var m2ts = Vyrob("vymyslený.m2ts", $"{Obraz} {Zvuk} -map 0 -map 1 -c:v mpeg2video -c:a ac3 -mpegts_m2ts_mode 1");
        var avi = Vyrob("vymyslený.avi", $"{Obraz} {Zvuk} -map 0 -map 1 -c:v mpeg4 -vtag XVID -c:a ac3");
        var vob = Vyrob("vymyslený.vob", $"{Obraz} {Zvuk} -map 0 -map 1 -c:v mpeg2video -c:a ac3 -f vob");
        foreach (var (cesta, kontajner) in new[] { (ts, "mpegts"), (m2ts, "m2ts"), (avi, "avi"), (vob, "mpeg") })
        {
            if (!File.Exists(cesta))
            {
                Console.WriteLine($"{Path.GetFileName(cesta)}: ffmpeg ho nevyrobil – vynechaný");
                continue;
            }

            var v = CitanieKontajnera.Citaj(cesta);
            Over.Pravda(v is { Udaje.Sirka: 1280, Udaje.Vyska: 720, Udaje.Snimky: 25 } && v.Udaje.Kontajner == kontajner, $"{kontajner}: obraz 1280×720 25 fps");
            Over.Pravda(v!.Udaje.DlzkaMs is > 5500 and < 6600, $"{kontajner}: dĺžka okolo 6 s ({v.Udaje.DlzkaMs} ms)");
            Over.Pravda(v.Stopy.Any(s => s is { Druh: DruhStopy.Zvuk, Kodek: "ac3" }), $"{kontajner}: zvuk AC-3");
        }

        if (File.Exists(ts))
        {
            Over.Pravda(CitanieKontajnera.Citaj(ts)!.Stopy.Single(s => s.Druh == DruhStopy.Zvuk) is { Jazyk: "sk", Kanaly: 6 }, "TS: jazyk a 5.1");
        }
    }

    // ---------------------------------------------------------------- streams put together

    private static byte[] Ts(int velkost, byte[] pmt, params (int Pid, byte[] Pes)[] pesy)
    {
        var pakety = new List<byte[]> { Paket(0, true, Sekcia([0x00, 0xB0, 0x0D, 0, 1, 0xC1, 0, 0, 0, 1, 0xE1, 0x00, 0, 0, 0, 0])), Paket(0x100, true, Sekcia(pmt)) };
        foreach (var (pid, pes) in pesy)
        {
            for (var i = 0; i < pes.Length; i += 184)
            {
                pakety.Add(Paket(pid, i == 0, pes[i..Math.Min(pes.Length, i + 184)]));
            }
        }

        return pakety.SelectMany(p => velkost == 192 ? [0, 0, 0, 0, .. p] : p).ToArray();
    }

    /// <summary>A packet of 188 bytes; a short payload is padded by an adaptation field of stuffing.</summary>
    private static byte[] Paket(int pid, bool zaciatok, byte[] obsah)
    {
        var p = new byte[188];
        p[0] = 0x47;
        p[1] = (byte)((zaciatok ? 0x40 : 0) | (pid >> 8));
        p[2] = (byte)pid;
        if (obsah.Length >= 184)
        {
            p[3] = 0x10;
            obsah.AsSpan(0, 184).CopyTo(p.AsSpan(4));
            return p;
        }

        p[3] = 0x30;
        var vypln = 183 - obsah.Length;
        p[4] = (byte)vypln;
        if (vypln > 0)
        {
            p[5] = 0;
            p.AsSpan(6, vypln - 1).Fill(0xFF);
        }

        obsah.CopyTo(p.AsSpan(5 + vypln));
        return p;
    }

    private static byte[] Sekcia(byte[] tabulka) => [0, .. tabulka];

    private static byte[] Pmt(int pcr, params (byte Typ, int Pid, byte[] Deskriptory)[] toky)
    {
        var telo = new List<byte> { 0, 1, 0xC1, 0, 0, (byte)(0xE0 | (pcr >> 8)), (byte)pcr, 0xF0, 0 };
        foreach (var (typ, pid, d) in toky)
        {
            telo.AddRange([typ, (byte)(0xE0 | (pid >> 8)), (byte)pid, (byte)(0xF0 | (d.Length >> 8)), (byte)d.Length, .. d]);
        }

        var dlzka = telo.Count + 4;
        return [0x02, (byte)(0xB0 | (dlzka >> 8)), (byte)dlzka, .. telo, 0, 0, 0, 0];
    }

    private static byte[] Pes(byte id, long pts, byte[] obsah)
    {
        byte[] p =
        [
            (byte)(0x21 | ((pts >> 29) & 0x0E)), (byte)(pts >> 22), (byte)(((pts >> 14) & 0xFE) | 1), (byte)(pts >> 7), (byte)(((pts << 1) & 0xFE) | 1),
        ];
        var dlzka = 3 + p.Length + obsah.Length;
        return [0, 0, 1, id, (byte)(dlzka >> 8), (byte)dlzka, 0x80, 0x80, 5, .. p, .. obsah];
    }

    private static byte[] Ps(params byte[][] pesy)
    {
        byte[] balik = [0, 0, 1, 0xBA, 0x44, 0, 4, 0, 4, 1, 0x01, 0x89, 0xC3, 0xF8];
        return pesy.SelectMany(p => balik.Concat(p)).ToArray();
    }

    private static byte[] Clpi(params (int Pid, byte Typ, string Jazyk)[] toky)
    {
        var program = new List<byte> { 0, 0, 0, 0, 0, 1, 0, 0, 0, 0, 0x01, 0x00, (byte)toky.Length, 0 };
        foreach (var (pid, typ, jazyk) in toky)
        {
            byte[] info = typ == 0x90 ? [typ, .. Encoding.ASCII.GetBytes(jazyk), 0] : [typ, 0x31, .. Encoding.ASCII.GetBytes(jazyk), 0];
            program.AddRange([(byte)(pid >> 8), (byte)pid, (byte)info.Length, .. info]);
        }

        var c = new byte[40];
        Encoding.ASCII.GetBytes("HDMV0200").CopyTo(c, 0);
        BinaryPrimitives.WriteUInt32BigEndian(c.AsSpan(12), 40);
        return [.. c, .. program];
    }

    private static byte[] Avi()
    {
        byte[] Kus(string id, byte[] data) => [.. Encoding.ASCII.GetBytes(id), .. U32((uint)data.Length), .. data, .. data.Length % 2 == 1 ? new byte[1] : []];
        byte[] Zoznam(string typ, params byte[][] deti) => Kus("LIST", [.. Encoding.ASCII.GetBytes(typ), .. deti.SelectMany(d => d)]);

        var avih = Kus("avih", [.. U32(40000), .. new byte[36]]);
        byte[] strhV = [.. Encoding.ASCII.GetBytes("vidsXVID"), .. new byte[12], .. U32(1), .. U32(25), .. U32(0), .. U32(25000), .. new byte[8], .. U32(0), .. new byte[8]];
        byte[] strfV = [.. U32(40), .. U32(720), .. U32(576), 1, 0, 24, 0, .. Encoding.ASCII.GetBytes("XVID"), .. new byte[20]];
        byte[] strhA = [.. Encoding.ASCII.GetBytes("auds"), .. new byte[16], .. U32(1152), .. U32(48000), .. U32(0), .. U32(41667), .. new byte[8], .. U32(0), .. new byte[8]];
        byte[] strfA = [0x55, 0, 2, 0, .. U32(48000), .. U32(24000), 1, 0, 0, 0];
        var hdrl = Zoznam("hdrl", avih, Zoznam("strl", Kus("strh", strhV), Kus("strf", strfV)), Zoznam("strl", Kus("strh", strhA), Kus("strf", strfA)));
        var info = Zoznam("INFO", Kus("INAM", Encoding.Latin1.GetBytes("Vymyslený film\0")));

        // the first video chunk: VOS (Advanced Simple@L5), VOL with pixel aspect 64:45 (extended), user data of Xvid
        var vol = new Bity().Z(0x00000120, 32).Z(0, 1).Z(17, 8).Z(0, 1).Z(15, 4).Z(64, 8).Z(45, 8).Z(0, 7).Bajty();
        byte[] video = [0, 0, 1, 0xB0, 0xF5, 0, 0, 1, 0xB5, 0x09, .. vol, 0, 0, 1, 0xB2, .. Encoding.ASCII.GetBytes("XviD0050"), 0, 0, 1, 0xB6, 0x10];
        byte[] zvuk = [0xFF, 0xFB, 0xB4, 0x04, .. new byte[100]];
        var videoKus = Kus("00dc", [.. video, .. new byte[300 - video.Length]]);
        var movi = Zoznam("movi", videoKus, Kus("01wb", zvuk));
        byte[] idx1 =
        [
            .. Encoding.ASCII.GetBytes("00dc"), .. U32(0x10), .. U32(4), .. U32(300),
            .. Encoding.ASCII.GetBytes("01wb"), .. U32(0), .. U32(312), .. U32((uint)zvuk.Length),
        ];
        var telo = (byte[])[.. Encoding.ASCII.GetBytes("AVI "), .. hdrl, .. info, .. movi, .. Kus("idx1", idx1)];
        return [.. Encoding.ASCII.GetBytes("RIFF"), .. U32((uint)telo.Length), .. telo];
    }

    private static byte[] U32(uint hodnota)
    {
        var b = new byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(b, hodnota);
        return b;
    }

    /// <summary>Bits written MSB first.</summary>
    private sealed class Bity
    {
        private readonly List<bool> _bity = [];

        public Bity Z(long hodnota, int pocet)
        {
            for (var i = pocet - 1; i >= 0; i--)
            {
                _bity.Add(((hodnota >> i) & 1) == 1);
            }

            return this;
        }

        public byte[] Bajty()
        {
            var bajty = new byte[(_bity.Count + 7) / 8];
            for (var i = 0; i < _bity.Count; i++)
            {
                if (_bity[i])
                {
                    bajty[i / 8] |= (byte)(0x80 >> (i % 8));
                }
            }

            return bajty;
        }
    }
}
