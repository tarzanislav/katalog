using System.Buffers.Binary;
using System.Text;
using Katalog.Jadro.Model;
using Katalog.Media;

namespace Katalog.Testy;

/// <summary>
/// KATALOG's own reader of Matroska and MP4 (owner, 8. 11. 2026): files put together here element by element (invented
/// content), so every value the reader gives is known.
/// </summary>
public sealed class VlastnaCitackaTesty
{
    /// <summary>The configuration of an invented HEVC video, 1920×800, Main 10, BT.709 (VPS, SPS, PPS).</summary>
    private static readonly byte[] Hvcc = Convert.FromHexString(
        "01022000000090000000000078f000fcfdfafa00000f03200001001840010c01ffff022000000300900000030000030078959809210001002e42010102" +
        "2000000300900000030000030078a003c080321365959a4932bc05a8080808200000030020000003032122000100074401c172b46240");

    [Test]
    public void MatroskaAkoZMkvmerge()
    {
        var subor = Mkv(
            Stopa(1, 101, 1, "V_MPEGH/ISO/HEVC", jazyk: "und", predvolena: 1, privat: Hvcc, trvanieSnimky: 40_000_000,
                deti: [E(0xE0, E(0xB0, U(1920)), E(0xBA, U(800)), E(0x9A, U(2)))]),
            Stopa(2, 102, 2, "A_EAC3", jazyk: "cze", predvolena: 1, deti: [E(0xE1, E(0xB5, F(48000)), E(0x9F, U(6)))]),
            Stopa(3, 103, 2, "A_DTS", jazyk: "eng", predvolena: 0, deti: [E(0xE1, E(0xB5, F(48000)), E(0x9F, U(6)))]),
            Stopa(4, 104, 17, "S_TEXT/UTF8", jazyk: "cze", nazov: "Forced", predvolena: 1, vynutena: 1),
            Stopa(5, 105, 17, "S_HDMV/PGS", jazyk: "eng", jazykBcp47: "en-US", predvolena: 0, sdh: 1));
        var info = CitanieMatroska.Citaj(new MemoryStream(subor))!;
        Over.Pravda(info is { Format: "Matroska", VerziaFormatu: "verzia 4", Aplikacia: "vymyslený muxer 1.0", Kniznica: "vymyslená knižnica" }, "hlavička");
        Over.Rovne(5460.0, Math.Round(info.DlzkaSekund!.Value, 3), "dĺžka 1 h 31 min");
        Over.Rovne(new DateTime(2026, 11, 8, 12, 24, 0, DateTimeKind.Utc), info.DatumKodovania, "dátum kódovania");

        var video = info.Stopy[0];
        Over.Pravda(video is { JeVideo: true, Kodek: "hevc", Format: "HEVC", Profil: "Main 10@L4@Main", Sirka: 1920, Vyska: 800, BitovaHlbka: 10,
            Podvzorkovanie: "4:2:0", PrimarneFarby: "BT.709", Prenos: "BT.709", Matica: "BT.709", FarebnyRozsah: "Limited", Prekladane: false }, "video z hvcC a SPS");
        Over.Rovne(25.0, video.Snimky, "25 snímok z DefaultDuration");
        Over.Rovne(3_010_000L, video.DatovyTok, "dátový tok zo štatistík");
        Over.Rovne(2_072_000_000L, video.VelkostStreamu, "veľkosť streamu zo štatistík");
        Over.Pravda(info.Stopy[1] is { Kodek: "eac3", ObchodnyNazov: "Dolby Digital Plus", Kanaly: 6, Vzorkovanie: 48000, Jazyk: "cs", Predvolena: true }, "E-AC-3");
        Over.Pravda(info.Stopy[2] is { Kodek: "dts-hd ma", ObchodnyNazov: "DTS-HD Master Audio", Jazyk: "en", Predvolena: false }, "DTS-HD MA z prvého rámca");
        Over.Pravda(info.Stopy[3] is { Druh: DruhStopy.Titulky, Kodek: "subrip", Nazov: "Forced", Vynutena: true, PocetSnimok: 21 }, "vynútené titulky");
        Over.Pravda(info.Stopy[4] is { Kodek: "hdmv_pgs_subtitle", Jazyk: "en", Sdh: true }, "PGS pre nepočujúcich (BCP 47)");

        var vysledok = PopisKontajnera.Vytvor(info);
        Over.Pravda(vysledok.Udaje is { Kontajner: "mkv", VideoKodek: "hevc", Sirka: 1920, Vyska: 800, Snimky: 25, Trieda: TriedaKvality.FHD }, "údaje knižnice");
        Over.Postupnost(["cs", "en", "cs", "en"], vysledok.Stopy.Select(s => s.Jazyk ?? "?"), "stopy pre knižnicu");
        Over.Postupnost(["Všeobecné", "Video", "Zvuk #1", "Zvuk #2", "Titulky #1", "Titulky #2"], vysledok.Podrobnosti.Select(s => s.Nazov), "sekcie");
        string Hodnota(string sekcia, string popis) => vysledok.Podrobnosti.Single(s => s.Nazov == sekcia).Polozky.Single(p => p.Popis == popis).Hodnota.Replace('\u00A0', ' ');
        Over.Rovne("1 h 31 min", Hodnota("Všeobecné", "Dĺžka"), "dĺžka");
        Over.Rovne("2,40:1", Hodnota("Video", "Pomer strán"), "pomer strán");
        Over.Rovne("3 010 kb/s", Hodnota("Video", "Dátový tok"), "tok videa");
        Over.Rovne("L R C LFE Ls Rs", Hodnota("Zvuk #1", "Rozloženie kanálov"), "rozloženie 5.1");
        Over.Rovne("48,0 kHz", Hodnota("Zvuk #1", "Vzorkovacia frekvencia"), "vzorkovanie");
        Over.Rovne("čeština", Hodnota("Titulky #1", "Jazyk"), "jazyk po slovensky");
    }

    [Test]
    public void MatroskaBezHlavickyANezmysly()
    {
        Over.Rovne<InfoSuboru?>(null, CitanieMatroska.Citaj(new MemoryStream([1, 2, 3])), "nie je Matroska");
        var zly = Mkv(Stopa(1, 1, 1, "V_MPEG4/ISO/AVC"));
        zly[^1] ^= 0xFF;
        foreach (var dlzka in new[] { 30, 60, 200, zly.Length - 1 })
        {
            _ = CitanieMatroska.Citaj(new MemoryStream(zly[..dlzka]));
            _ = CitanieMp4.Citaj(new MemoryStream(Mp4()[..dlzka]));
        }
        var nahodne = new byte[4096];
        new Random(7).NextBytes(nahodne);
        nahodne[0] = 0x1A;
        nahodne[1] = 0x45;
        nahodne[2] = 0xDF;
        nahodne[3] = 0xA3;
        Over.Rovne<InfoSuboru?>(null, CitanieMatroska.Citaj(new MemoryStream(nahodne)), "náhodné bajty sú null");
    }

    [Test]
    public void Mp4SKoncovymMoov()
    {
        var subor = Mp4();
        var info = CitanieMp4.Citaj(new MemoryStream(subor))!;
        Over.Pravda(info is { Format: "MPEG-4", Aplikacia: "vymyslený kóder" }, "hlavička");
        Over.Rovne(100.0, info.DlzkaSekund, "dĺžka z mvhd");
        var video = info.Stopy[0];
        Over.Pravda(video is { JeVideo: true, Kodek: "hevc", Sirka: 1920, Vyska: 800, BitovaHlbka: 10, Profil: "Main 10@L4@Main", PrimarneFarby: "BT.2020", Hdr: Hdr.Hdr10 },
            "video s colr nclx (HDR10)");
        Over.Rovne(25.0, video.Snimky, "snímky zo stts");
        Over.Rovne(2500L, video.PocetSnimok, "počet snímok zo stsz");
        Over.Rovne(2500L * 1000, video.VelkostStreamu, "veľkosť streamu");
        Over.Pravda(info.Stopy[1] is { Druh: DruhStopy.Zvuk, Kodek: "eac3", Kanaly: 6, Vzorkovanie: 48000, Jazyk: "cs" }, "zvuk E-AC-3 v češtine");
        Over.Rovne<InfoSuboru?>(null, CitanieMp4.Citaj(new MemoryStream(Encoding.ASCII.GetBytes("nie je to mp4 súbor vôbec"))), "nie je MP4");
    }

    [Test]
    public void DtsRozsirenia()
    {
        InfoStopy S() => new() { Kodek = "dts", Format = "DTS" };
        var ma = S();
        AnalyzaRamcov.Dts([0x7F, 0xFE, 0x80, 0x01, 0, 0, 0x64, 0x58, 0x20, 0x25, 1, 2, 0x41, 0xA2, 0x95, 0x47], ma);
        Over.Rovne("dts-hd ma", ma.Kodek, "XLL je Master Audio");
        var hra = S();
        AnalyzaRamcov.Dts([0x64, 0x58, 0x20, 0x25, 0x65, 0x5E, 0x31, 0x5E], hra);
        Over.Rovne("dts-hd hra", hra.Kodek, "XBR je High Resolution");
        var jadro = S();
        AnalyzaRamcov.Dts([0x7F, 0xFE, 0x80, 0x01, 0x41, 0xA2, 0x95, 0x47], jadro);
        Over.Rovne("dts", jadro.Kodek, "bez rozširujúceho prúdu len DTS");
    }

    [Test]
    public void AtmosDtsXAHdr10Plus()
    {
        // E-AC-3 5.1 with JOC: strmtyp 0, frmsiz, fscod 0, 6 blocks, acmod 7, LFE, bsid 16, dialnorm 24, addbsi with the flag
        var bity = new ZapisBitov();
        bity.Zapis(0x0B77, 16).Zapis(0, 2).Zapis(0, 3).Zapis(100, 11).Zapis(0, 2).Zapis(3, 2).Zapis(7, 3).Zapis(1, 1).Zapis(16, 5)
            .Zapis(24, 5).Zapis(0, 1).Zapis(0, 1).Zapis(0, 1).Zapis(1, 1).Zapis(1, 6).Zapis(0, 7).Zapis(1, 1).Zapis(16, 8).Zapis(0, 16);
        var eac3 = new InfoStopy { Kodek = "eac3", Format = "E-AC-3", PrvyRamec = bity.Bajty() };
        eac3.AnalyzujRamec();
        Over.Pravda(eac3 is { Kodek: "eac3 atmos", Atmos: true, Kanaly: 6, RozlozenieKanalov: "L R C LFE Ls Rs", Dialogy: -24 }, "E-AC-3 s Atmos (JOC)");
        Over.Rovne("Dolby Digital Plus s Dolby Atmos", eac3.ObchodnyNazov, "obchodný názov");

        var bezAtmos = new ZapisBitov();
        bezAtmos.Zapis(0x0B77, 16).Zapis(0, 2).Zapis(0, 3).Zapis(100, 11).Zapis(0, 2).Zapis(3, 2).Zapis(2, 3).Zapis(0, 1).Zapis(16, 5)
            .Zapis(31, 5).Zapis(0, 1).Zapis(0, 1).Zapis(0, 1).Zapis(0, 1).Zapis(0, 16);
        var stereo = new InfoStopy { Kodek = "eac3", PrvyRamec = bezAtmos.Bajty() };
        stereo.AnalyzujRamec();
        Over.Pravda(stereo is { Kodek: "eac3", Atmos: false, Kanaly: 2, RozlozenieKanalov: "L R", Dialogy: -31 }, "E-AC-3 stereo bez Atmosu");

        // TrueHD: access unit, major sync with four substreams and the 16-channel presentation
        byte[] truehd = [0x10, 0x20, 0x00, 0x00, 0xF8, 0x72, 0x6F, 0xBA, 0, 0, 0, 0, 0xB7, 0x52, 0, 0, 0, 0, 0x80, 0, 0x40, 0x80, 0, 0];
        var thd = new InfoStopy { Kodek = "truehd", PrvyRamec = truehd };
        thd.AnalyzujRamec();
        Over.Pravda(thd is { Kodek: "truehd atmos", Atmos: true, ObchodnyNazov: "Dolby TrueHD s Dolby Atmos" }, "TrueHD s Atmos");
        truehd[21] = 0x00;
        var thd2 = new InfoStopy { Kodek = "truehd", PrvyRamec = truehd };
        thd2.AnalyzujRamec();
        Over.Pravda(thd2 is { Kodek: "truehd", Atmos: false }, "TrueHD bez Atmosu");

        // DTS:X: XLL with its X marker; IMAX Enhanced
        var x = new InfoStopy { Kodek = "dts" };
        AnalyzaRamcov.Dts([0x64, 0x58, 0x20, 0x25, 0x41, 0xA2, 0x95, 0x47, 0, 0, 0x02, 0x00, 0x08, 0x50], x);
        Over.Pravda(x is { Kodek: "dts:x", ObchodnyNazov: "DTS:X" }, "DTS:X");
        var imax = new InfoStopy { Kodek = "dts" };
        AnalyzaRamcov.Dts([0x64, 0x58, 0x20, 0x25, 0x41, 0xA2, 0x95, 0x47, 0xF1, 0x40, 0x00, 0xD0], imax);
        Over.Rovne("DTS:X IMAX Enhanced", imax.ObchodnyNazov, "IMAX Enhanced");

        // HEVC: an SEI with HDR10+ (T.35: B5 003C 0001 04) and an RPU of Dolby Vision, NAL sizes in 4 bytes
        byte[] sei = [0x4E, 0x01, 0x04, 0x08, 0xB5, 0x00, 0x3C, 0x00, 0x01, 0x04, 0x01, 0x40, 0x80];
        byte[] rpu = [0x7C, 0x01, 0x19, 0x08];
        byte[] obraz = [0, 0, 0, (byte)sei.Length, .. sei, 0, 0, 0, (byte)rpu.Length, .. rpu];
        var video = new InfoStopy { Druh = DruhStopy.Video, Kodek = "hevc", JeVideo = true, Prenos = "PQ (SMPTE ST 2084)", Hdr = Hdr.Hdr10, PrvyRamec = obraz };
        video.AnalyzujRamec();
        Over.Pravda(video is { Hdr10Plus: true, Hdr: Hdr.DolbyVision }, "HDR10+ a Dolby Vision v obraze");
        var vysledok = PopisKontajnera.Vytvor(new InfoSuboru { Format = "Matroska", Velkost = 1, Stopy = { video } });
        Over.Rovne(Hdr.DolbyVision, vysledok.Udaje.Hdr, "Dolby Vision má prednosť");
        Over.Pravda(vysledok.Podrobnosti.Single(s => s.Nazov == "Video").Polozky.Single(p => p.Popis == "HDR").Hodnota.Contains("HDR10+", StringComparison.Ordinal),
            "HDR v podrobnostiach");
    }

    [Test]
    public void FormatovaniePodrobnosti()
    {
        Over.Rovne("2,26 GiB", PopisKontajnera.Velkost(2_426_656_768), "GiB");
        Over.Rovne("168 MiB", PopisKontajnera.Velkost(176_160_768), "MiB nad 100");
        Over.Rovne("37,0 KiB", PopisKontajnera.Velkost(37_888), "KiB");
        Over.Rovne("454 bajtov", PopisKontajnera.Velkost(454), "bajty");
        Over.Rovne("1 h 31 min", PopisKontajnera.Dlzka(5460), "hodiny");
        Over.Rovne("3 525 kb/s", PopisKontajnera.Tok(3_525_000)!.Replace(' ', ' '), "kb/s");
        Over.Rovne("16:9", PopisKontajnera.PomerStran(1920 / 1080.0), "16:9");
        Over.Rovne("25,000 FPS", PopisKontajnera.Fps(25), "FPS");
    }

    // ---------------------------------------------------------------- Matroska put together

    private static byte[] Mkv(params byte[][] stopy)
    {
        var ebml = E(0x1A45DFA3, E(0x4286, U(1)), E(0x4282, S("matroska")), E(0x4287, U(4)), E(0x4285, U(2)));
        var info = E(0x1549A966, E(0x2AD7B1, U(1_000_000)), E(0x4489, F(5_460_000)), E(0x4D80, S("vymyslená knižnica")), E(0x5741, S("vymyslený muxer 1.0")),
            E(0x4461, I64((long)(new DateTime(2026, 11, 8, 12, 24, 0, DateTimeKind.Utc) - new DateTime(2001, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalMilliseconds * 1_000_000)));
        var tracks = E(0x1654AE6B, stopy);
        var znacky = E(0x1254C367,
            Znacka(101, ("BPS", "3010000"), ("DURATION", "01:31:00.000000000"), ("NUMBER_OF_FRAMES", "136500"), ("NUMBER_OF_BYTES", "2072000000")),
            Znacka(102, ("BPS", "256000"), ("NUMBER_OF_BYTES", "176160768")),
            Znacka(104, ("BPS", "0"), ("NUMBER_OF_FRAMES", "21"), ("NUMBER_OF_BYTES", "454")));
        // a cluster with the first DTS frame: core, then the extension substream with XLL
        byte[] dts = [0x7F, 0xFE, 0x80, 0x01, 0, 0, 0, 0, 0x64, 0x58, 0x20, 0x25, 0, 0, 0, 0, 0x41, 0xA2, 0x95, 0x47];
        var cluster = E(0x1F43B675, E(0xE7, U(0)), E(0xA3, [0x83, 0, 0, 0x80, .. dts]));

        // the tags lie after the cluster, the seek head points to them
        var predTagmi = info.Length + tracks.Length + cluster.Length;
        var seek = E(0x114D9B74, E(0x4DBB, E(0x53AB, [0x12, 0x54, 0xC3, 0x67]), E(0x53AC, U((ulong)(predTagmi + 64)))));
        var vypln = 64 - seek.Length;
        var obsah = seek.Concat(E(0xEC, new byte[vypln - 9])).Concat(info).Concat(tracks).Concat(cluster).Concat(znacky).ToArray();
        return [.. ebml, .. E(0x18538067, obsah)];
    }

    private static byte[] Stopa(int cislo, ulong uid, int typ, string kodek, string? jazyk = null, string? jazykBcp47 = null, string? nazov = null,
        int predvolena = 1, int vynutena = 0, int sdh = 0, byte[]? privat = null, ulong? trvanieSnimky = null, byte[][]? deti = null)
    {
        var casti = new List<byte[]> { E(0xD7, U((ulong)cislo)), E(0x73C5, U(uid)), E(0x83, U((ulong)typ)), E(0x86, S(kodek)), E(0x88, U((ulong)predvolena)) };
        if (jazyk is not null)
        {
            casti.Add(E(0x22B59C, S(jazyk)));
        }

        if (jazykBcp47 is not null)
        {
            casti.Add(E(0x22B59D, S(jazykBcp47)));
        }

        if (nazov is not null)
        {
            casti.Add(E(0x536E, S(nazov)));
        }

        casti.Add(E(0x55AA, U((ulong)vynutena)));
        casti.Add(E(0x55AB, U((ulong)sdh)));
        if (privat is not null)
        {
            casti.Add(E(0x63A2, privat));
        }

        if (trvanieSnimky is { } t)
        {
            casti.Add(E(0x23E383, U(t)));
        }

        casti.AddRange(deti ?? []);
        return E(0xAE, [.. casti]);
    }

    private static byte[] Znacka(ulong uid, params (string Meno, string Hodnota)[] hodnoty) =>
        E(0x7373, [E(0x63C0, E(0x63C5, U(uid))), .. hodnoty.Select(h => E(0x67C8, E(0x45A3, S(h.Meno)), E(0x4487, S(h.Hodnota))))]);

    /// <summary>An EBML element with an 8-byte size.</summary>
    private static byte[] E(uint id, params byte[][] deti)
    {
        var obsah = deti.SelectMany(d => d).ToArray();
        var idBajty = BitConverter.GetBytes(id).Reverse().SkipWhile(b => b == 0).ToArray();
        var velkost = new byte[8];
        BinaryPrimitives.WriteUInt64BigEndian(velkost, (ulong)obsah.Length);
        velkost[0] = 0x01;
        return [.. idBajty, .. velkost, .. obsah];
    }

    private static byte[] U(ulong hodnota) => BitConverter.GetBytes(hodnota).Reverse().SkipWhile(b => b == 0).DefaultIfEmpty((byte)0).ToArray();

    private static byte[] I64(long hodnota)
    {
        var b = new byte[8];
        BinaryPrimitives.WriteInt64BigEndian(b, hodnota);
        return b;
    }

    private static byte[] F(double hodnota)
    {
        var b = new byte[8];
        BinaryPrimitives.WriteDoubleBigEndian(b, hodnota);
        return b;
    }

    private static byte[] S(string text) => Encoding.UTF8.GetBytes(text);

    // ---------------------------------------------------------------- MP4 put together

    private static byte[] Mp4()
    {
        var ftyp = Box("ftyp", [.. Encoding.ASCII.GetBytes("isom"), 0, 0, 2, 0, .. Encoding.ASCII.GetBytes("isomiso2mp41")]);
        var mdat = Box("mdat", new byte[256]);
        var mvhd = Box("mvhd", [0, 0, 0, 0, .. B32(0), .. B32(0), .. B32(1000), .. B32(100_000), .. new byte[80]]);

        // HEVC video with colr nclx (BT.2020, PQ, BT.2020 nc): 2500 frames of 1000 bytes at 25 fps
        var colr = Box("colr", [.. Encoding.ASCII.GetBytes("nclx"), 0, 9, 0, 16, 0, 9, 0]);
        var hvc1 = Popis("hvc1", [.. new byte[16], 0x07, 0x80, 0x03, 0x20, .. new byte[50], .. Box("hvcC", Hvcc), .. colr]);
        var video = Trak(1, "vide", 25_000, 2_500_000, 0, hvc1,
            Box("stts", [0, 0, 0, 0, .. B32(1), .. B32(2500), .. B32(1000)]),
            Box("stsz", [0, 0, 0, 0, .. B32(1000), .. B32(2500)]));

        // E-AC-3 5.1 at 48 kHz, Czech
        var ec3 = Popis("ec-3", [0, 0, .. new byte[6], 0, 6, 0, 16, 0, 0, 0, 0, .. B32(48000u << 16)]);
        var zvuk = Trak(2, "soun", 48000, 4_800_000, Jazyk("ces"), ec3,
            Box("stts", [0, 0, 0, 0, .. B32(1), .. B32(3125), .. B32(1536)]),
            Box("stsz", [0, 0, 0, 0, .. B32(0), .. B32(2), .. B32(100), .. B32(200)]));

        var data = Box("data", [0, 0, 0, 1, 0, 0, 0, 0, .. Encoding.UTF8.GetBytes("vymyslený kóder")]);
        var udta = Box("udta", Box("meta", [0, 0, 0, 0, .. Box("ilst", Box("©too", data))]));
        var moov = Box("moov", [.. mvhd, .. video, .. zvuk, .. udta]);
        return [.. ftyp, .. mdat, .. moov];
    }

    private static byte[] Trak(int id, string handler, uint mierka, uint trvanie, ushort jazyk, byte[] popis, params byte[][] tabulky)
    {
        var tkhd = Box("tkhd", [0, 0, 0, 3, .. B32(0), .. B32(0), .. B32((uint)id), .. new byte[60], .. B32(1920u << 16), .. B32(800u << 16)]);
        var mdhd = Box("mdhd", [0, 0, 0, 0, .. B32(0), .. B32(0), .. B32(mierka), .. B32(trvanie), (byte)(jazyk >> 8), (byte)jazyk, 0, 0]);
        var hdlr = Box("hdlr", [0, 0, 0, 0, 0, 0, 0, 0, .. Encoding.ASCII.GetBytes(handler), .. new byte[13]]);
        var stsd = Box("stsd", [0, 0, 0, 0, .. B32(1), .. popis]);
        var stbl = Box("stbl", [.. stsd, .. tabulky.SelectMany(t => t)]);
        return Box("trak", [.. tkhd, .. Box("mdia", [.. mdhd, .. hdlr, .. Box("minf", stbl)])]);
    }

    private static byte[] Popis(string format, byte[] obsah) => Box(format, [0, 0, 0, 0, 0, 0, 0, 1, .. obsah]);

    private static ushort Jazyk(string kod) => (ushort)(((kod[0] - 0x60) << 10) | ((kod[1] - 0x60) << 5) | (kod[2] - 0x60));

    private static byte[] Box(string typ, byte[] obsah) => [.. B32((uint)(obsah.Length + 8)), .. Encoding.Latin1.GetBytes(typ), .. obsah];

    private static byte[] B32(uint hodnota)
    {
        var b = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(b, hodnota);
        return b;
    }

    /// <summary>Bits written MSB first (a frame of a codec put together).</summary>
    private sealed class ZapisBitov
    {
        private readonly List<bool> _bity = [];

        public ZapisBitov Zapis(long hodnota, int pocet)
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
