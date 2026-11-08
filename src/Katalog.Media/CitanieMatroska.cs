using System.Buffers.Binary;
using System.Globalization;
using System.Text;
using Katalog.Jadro.Model;

namespace Katalog.Media;

/// <summary>
/// KATALOG's own reader of Matroska and WebM files (EBML): only the head of the file (Info, Tracks) and the tags of the
/// tracks (mkvmerge writes the size, data rate and count of frames of each track there) are read – usually a few
/// kilobytes at the start and a jump to the end, instead of a run of ffprobe. For DTS the first audio frame tells
/// DTS-HD MA and HRA apart. A file it cannot follow gives null (ffprobe reads it then).
/// </summary>
public static class CitanieMatroska
{
    private const uint IdEbml = 0x1A45DFA3;
    private const uint IdSegment = 0x18538067;
    private const uint IdSeekHead = 0x114D9B74;
    private const uint IdInfo = 0x1549A966;
    private const uint IdTracks = 0x1654AE6B;
    private const uint IdTags = 0x1254C367;
    private const uint IdCluster = 0x1F43B675;

    /// <summary>A master element bigger than this is not read (a broken size).</summary>
    private const long NajviacPrvok = 32L << 20;

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
        var dlzkaSuboru = subor.Length;
        var hlava = Hlavicka(subor, 0);
        if (hlava is not { Id: IdEbml, Velkost: { } velkostEbml } ebml || velkostEbml > 4096)
        {
            return null;
        }

        var info = new InfoSuboru { Velkost = dlzkaSuboru, Format = "Matroska" };
        foreach (var (id, data) in Deti(CitajData(subor, ebml.Data, ebml.Velkost)))
        {
            if (id == 0x4282)
            {
                info.Format = Text(data) == "webm" ? "WebM" : "Matroska";
            }
            else if (id == 0x4287)
            {
                info.VerziaFormatu = "verzia " + Cislo(data).ToString(CultureInfo.InvariantCulture);
            }
        }

        var segment = Hlavicka(subor, ebml.Data + velkostEbml);
        if (segment is not { Id: IdSegment })
        {
            return null;
        }

        var zaciatok = segment.Value.Data;
        var koniec = segment.Value.Velkost is { } v and > 0 ? Math.Min(dlzkaSuboru, zaciatok + v) : dlzkaSuboru;
        var precitane = new HashSet<uint>();
        var odkazy = new List<(uint Id, long Pozicia)>();
        long? prvyCluster = null;
        var stopy = new Dictionary<ulong, InfoStopy>();
        var statistiky = new Dictionary<ulong, Dictionary<string, string>>();

        void Spracuj(uint id, long data, long velkost)
        {
            if (velkost > NajviacPrvok || !precitane.Add(id) && id != IdSeekHead)
            {
                return;
            }

            var obsah = CitajData(subor, data, velkost);
            switch (id)
            {
                case IdSeekHead:
                    foreach (var (sid, seek) in Deti(obsah).Where(d => d.Id == 0x4DBB))
                    {
                        uint ciel = 0;
                        long pozicia = -1;
                        foreach (var (pid, pdata) in Deti(seek))
                        {
                            if (pid == 0x53AB)
                            {
                                ciel = (uint)Cislo(pdata);
                            }
                            else if (pid == 0x53AC)
                            {
                                pozicia = (long)Cislo(pdata);
                            }
                        }

                        if (pozicia >= 0)
                        {
                            odkazy.Add((ciel, zaciatok + pozicia));
                        }
                    }

                    break;
                case IdInfo:
                    CitajInfo(obsah, info);
                    break;
                case IdTracks:
                    foreach (var (tid, entry) in Deti(obsah).Where(d => d.Id == 0xAE))
                    {
                        var stopa = CitajStopu(entry);
                        stopy[stopa.Uid != 0 ? stopa.Uid : (ulong)stopa.Id] = stopa;
                        info.Stopy.Add(stopa);
                    }

                    break;
                case IdTags:
                    CitajZnacky(obsah, statistiky);
                    break;
            }
        }

        // the elements at the start up to the first cluster, then those the seek heads point to (tags are often at the end)
        var pos = zaciatok;
        while (pos < koniec)
        {
            var h = Hlavicka(subor, pos);
            if (h is not { } prvok)
            {
                break;
            }

            if (prvok.Id == IdCluster)
            {
                prvyCluster = pos;
                break;
            }

            if (prvok.Velkost is not { } velkost)
            {
                break;
            }

            if (prvok.Id is IdSeekHead or IdInfo or IdTracks or IdTags)
            {
                Spracuj(prvok.Id, prvok.Data, velkost);
            }

            pos = prvok.Data + velkost;
        }

        var navstivene = new HashSet<long>();
        for (var i = 0; i < odkazy.Count && i < 256; i++)
        {
            var (id, pozicia) = odkazy[i];
            var potrebne = id is IdInfo or IdTracks or IdTags && !precitane.Contains(id) || id == IdSeekHead && pozicia > pos;
            if (potrebne && navstivene.Add(pozicia))
            {
                if (Hlavicka(subor, pozicia) is { Velkost: { } velkost } prvok && prvok.Id == id)
                {
                    Spracuj(prvok.Id, prvok.Data, velkost);
                }
            }

            if (id == IdCluster && prvyCluster is null)
            {
                prvyCluster = pozicia;
            }
        }

        if (info.Stopy.Count == 0)
        {
            return null;
        }

        foreach (var (uid, znacky) in statistiky)
        {
            if (stopy.TryGetValue(uid, out var stopa))
            {
                Statistiky(stopa, znacky);
            }
        }

        if (prvyCluster is { } cluster && info.Stopy.Any(s => s.Kodek == "dts"))
        {
            DtsZBlokov(subor, cluster, koniec, info.Stopy);
        }

        foreach (var s in info.Stopy)
        {
            s.DlzkaSekund ??= info.DlzkaSekund;
            if (s.DatovyTok is null && s.VelkostStreamu is { } b && s.DlzkaSekund is > 0)
            {
                s.DatovyTok = (long)Math.Round(b * 8 / s.DlzkaSekund.Value);
            }
        }

        return info;
    }

    private static void CitajInfo(byte[] obsah, InfoSuboru info)
    {
        double mierka = 1_000_000;
        double? trvanie = null;
        foreach (var (id, data) in Deti(obsah))
        {
            switch (id)
            {
                case 0x2AD7B1:
                    mierka = Cislo(data);
                    break;
                case 0x4489:
                    trvanie = Desatinne(data);
                    break;
                case 0x7BA9:
                    info.Nazov = Text(data);
                    break;
                case 0x4D80:
                    info.Kniznica = Text(data);
                    break;
                case 0x5741:
                    info.Aplikacia = Text(data);
                    break;
                case 0x4461 when data.Length == 8:
                    var ns = BinaryPrimitives.ReadInt64BigEndian(data);
                    info.DatumKodovania = new DateTime(2001, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddTicks(ns / 100);
                    break;
            }
        }

        if (trvanie is > 0)
        {
            info.DlzkaSekund = trvanie.Value * mierka / 1e9;
        }
    }

    private static InfoStopy CitajStopu(byte[] entry)
    {
        var s = new InfoStopy { Predvolena = true };
        string? jazyk = "eng";
        string? bcp47 = null;
        byte[]? privat = null;
        var typ = 0;
        var priznakVynutena = false;
        foreach (var (id, data) in Deti(entry))
        {
            switch (id)
            {
                case 0xD7:
                    s.Id = (int)Cislo(data);
                    break;
                case 0x73C5:
                    s.Uid = Cislo(data);
                    break;
                case 0x83:
                    typ = (int)Cislo(data);
                    break;
                case 0x88:
                    s.Predvolena = Cislo(data) == 1;
                    break;
                case 0x55AA:
                    priznakVynutena = Cislo(data) == 1;
                    break;
                case 0x55AB:
                    s.Sdh = Cislo(data) == 1;
                    break;
                case 0x536E:
                    s.Nazov = Text(data);
                    break;
                case 0x22B59C:
                    jazyk = Text(data);
                    break;
                case 0x22B59D:
                    bcp47 = Text(data);
                    break;
                case 0x86:
                    s.IdKodeku = Text(data);
                    break;
                case 0x63A2:
                    privat = data;
                    break;
                case 0x23E383 when Cislo(data) is > 0UL and var ns:
                    s.Snimky = 1e9 / ns;
                    s.KonstantneSnimky = true;
                    break;
                case 0xE0:
                    CitajVideo(data, s);
                    break;
                case 0xE1:
                    CitajZvuk(data, s);
                    break;
                case 0x41E4:
                    // BlockAdditionMapping: Dolby Vision configuration (dvcC, dvvC)
                    foreach (var (mid, mdata) in Deti(data))
                    {
                        if (mid == 0x41E7 && Cislo(mdata) is 0x64766343 or 0x64767643 or 0x64767743)
                        {
                            s.Hdr = Hdr.DolbyVision;
                        }
                        else if (mid == 0x41ED && mdata.Length >= 4)
                        {
                            s.HdrPopis = $"Dolby Vision, verzia {mdata[0]}.{mdata[1]}, profil {mdata[2] >> 1}, úroveň {((mdata[2] & 1) << 5) | (mdata[3] >> 3)}";
                        }
                    }

                    break;
            }
        }

        s.Vynutena = priznakVynutena;
        s.Jazyk = Jazyky.Normalizuj(bcp47?.Split('-')[0] ?? jazyk);
        s.Druh = typ switch { 1 => DruhStopy.Video, 2 => DruhStopy.Zvuk, 17 => DruhStopy.Titulky, _ => null };
        s.JeVideo = typ == 1;
        if (s.HdrPopis is not null && s.Hdr != Hdr.DolbyVision)
        {
            s.HdrPopis = null;
        }

        Kodek(s, privat);
        if (s.JeVideo && s.Sirka is > 0 && s.Vyska is > 0)
        {
            s.PomerStran ??= s.Sirka.Value / (double)s.Vyska.Value;
        }

        return s;
    }

    private static void CitajVideo(byte[] video, InfoStopy s)
    {
        int? zobrazSirka = null, zobrazVyska = null;
        var jednotka = 0;
        foreach (var (id, data) in Deti(video))
        {
            switch (id)
            {
                case 0xB0:
                    s.Sirka = (int)Cislo(data);
                    break;
                case 0xBA:
                    s.Vyska = (int)Cislo(data);
                    break;
                case 0x54B0:
                    zobrazSirka = (int)Cislo(data);
                    break;
                case 0x54BA:
                    zobrazVyska = (int)Cislo(data);
                    break;
                case 0x54B2:
                    jednotka = (int)Cislo(data);
                    break;
                case 0x9A:
                    s.Prekladane = Cislo(data) switch { 1 => true, 2 => false, _ => null };
                    break;
                case 0x55B0:
                    CitajFarby(data, s);
                    break;
            }
        }

        if (zobrazSirka is > 0 && zobrazVyska is > 0 && jednotka is 0 or 3)
        {
            s.PomerStran = zobrazSirka.Value / (double)zobrazVyska.Value;
        }
    }

    private static void CitajFarby(byte[] farby, InfoStopy s)
    {
        int primarne = 2, prenos = 2, matica = 2;
        int? horizontalne = null, vertikalne = null;
        var cll = false;
        foreach (var (id, data) in Deti(farby))
        {
            switch (id)
            {
                case 0x55B1:
                    matica = (int)Cislo(data);
                    break;
                case 0x55B2 when Cislo(data) is > 0UL and < 17UL and var b:
                    s.BitovaHlbka = (int)b;
                    break;
                case 0x55B3:
                    horizontalne = (int)Cislo(data);
                    break;
                case 0x55B4:
                    vertikalne = (int)Cislo(data);
                    break;
                case 0x55B9:
                    s.FarebnyRozsah = Cislo(data) switch { 1 => "Limited", 2 => "Full", _ => s.FarebnyRozsah };
                    break;
                case 0x55BA:
                    prenos = (int)Cislo(data);
                    break;
                case 0x55BB:
                    primarne = (int)Cislo(data);
                    break;
                case 0x55BC or 0x55D0:
                    cll = true;
                    break;
            }
        }

        if (horizontalne is { } h && vertikalne is { } v)
        {
            s.Podvzorkovanie = (h, v) switch { (1, 1) => "4:2:0", (1, 0) => "4:2:2", (0, 0) => "4:4:4", _ => s.Podvzorkovanie };
        }

        KodekyVidea.Farby(s, primarne, prenos, matica);
        if (s.Hdr == Hdr.Hdr10 && cll)
        {
            s.HdrPopis ??= "HDR10 (SMPTE ST 2086, MaxCLL)";
        }
    }

    private static void CitajZvuk(byte[] zvuk, InfoStopy s)
    {
        foreach (var (id, data) in Deti(zvuk))
        {
            switch (id)
            {
                case 0xB5:
                    s.Vzorkovanie = Desatinne(data);
                    break;
                case 0x78B5 when Desatinne(data) is > 0 and var vystup:
                    s.Vzorkovanie = vystup;
                    break;
                case 0x9F:
                    s.Kanaly = (int)Cislo(data);
                    break;
            }
        }
    }

    /// <summary>The codec ID of Matroska and the codec's own configuration.</summary>
    private static void Kodek(InfoStopy s, byte[]? privat)
    {
        var id = s.IdKodeku ?? "";
        (s.Kodek, s.Format, s.FormatInfo, s.ObchodnyNazov) = id switch
        {
            "V_MPEGH/ISO/HEVC" => ("hevc", "HEVC", "High Efficiency Video Coding", null),
            "V_MPEG4/ISO/AVC" => ("h264", "AVC", "Advanced Video Codec", null),
            "V_AV1" => ("av1", "AV1", "AOMedia Video 1", null),
            "V_VP9" => ("vp9", "VP9", null, null),
            "V_VP8" => ("vp8", "VP8", null, null),
            "V_MPEG2" => ("mpeg2video", "MPEG Video", "MPEG-2", null),
            "V_MPEG1" => ("mpeg1video", "MPEG Video", "MPEG-1", null),
            "V_MPEG4/ISO/ASP" or "V_MPEG4/ISO/SP" or "V_MPEG4/ISO/AP" => ("mpeg4", "MPEG-4 Visual", null, null),
            "V_MS/VFW/FOURCC" => FourCc(privat),
            "V_MJPEG" => ("mjpeg", "JPEG", null, null),
            "A_AC3" or "A_AC3/BSID9" or "A_AC3/BSID10" => ("ac3", "AC-3", "Audio Coding 3", "Dolby Digital"),
            "A_EAC3" => ("eac3", "E-AC-3", "Enhanced AC-3", "Dolby Digital Plus"),
            "A_TRUEHD" => ("truehd", "MLP FBA", "Meridian Lossless Packing FBA", "Dolby TrueHD"),
            "A_DTS" or "A_DTS/EXPRESS" or "A_DTS/LOSSLESS" => ("dts", "DTS", "Digital Theater Systems", "DTS"),
            "A_FLAC" => ("flac", "FLAC", "Free Lossless Audio Codec", null),
            "A_OPUS" => ("opus", "Opus", null, null),
            "A_VORBIS" => ("vorbis", "Vorbis", null, null),
            "A_MPEG/L3" => ("mp3", "MPEG Audio", "MPEG-1 Layer 3", "MP3"),
            "A_MPEG/L2" => ("mp2", "MPEG Audio", "MPEG-1 Layer 2", null),
            var a when a.StartsWith("A_AAC", StringComparison.Ordinal) => ("aac", "AAC", "Advanced Audio Codec", null),
            var a when a.StartsWith("A_PCM", StringComparison.Ordinal) => ("pcm_s16le", "PCM", null, null),
            "S_TEXT/UTF8" => ("subrip", "UTF-8", "UTF-8 Plain Text", null),
            "S_TEXT/ASS" => ("ass", "ASS", "Advanced Sub Station Alpha", null),
            "S_TEXT/SSA" => ("ssa", "SSA", "Sub Station Alpha", null),
            "S_TEXT/WEBVTT" => ("webvtt", "WebVTT", null, null),
            "S_HDMV/PGS" => ("hdmv_pgs_subtitle", "PGS", "Presentation Graphic Stream", null),
            "S_VOBSUB" => ("dvd_subtitle", "VobSub", "Picture based subtitle format used on DVDs", null),
            "S_HDMV/TEXTST" => ("hdmv_text_subtitle", "TextST", null, null),
            _ => (id.Length > 2 ? id[2..].ToLowerInvariant() : null, id.Length > 2 ? id[2..] : null, null, null),
        };

        if (privat is null)
        {
            return;
        }

        switch (id)
        {
            case "V_MPEGH/ISO/HEVC":
                KodekyVidea.Hevc(privat, s);
                break;
            case "V_MPEG4/ISO/AVC":
                KodekyVidea.Avc(privat, s);
                break;
            case "V_AV1":
                KodekyVidea.Av1(privat, s);
                break;
            case var a when a.StartsWith("A_AAC", StringComparison.Ordinal):
                s.Profil = KodekyVidea.ProfilAac(privat);
                break;
        }
    }

    private static (string?, string?, string?, string?) FourCc(byte[]? bitmapinfo)
    {
        if (bitmapinfo is not { Length: >= 20 })
        {
            return (null, null, null, null);
        }

        var fourcc = Encoding.ASCII.GetString(bitmapinfo, 16, 4).ToUpperInvariant();
        return fourcc switch
        {
            "XVID" or "DIVX" or "DX50" or "FMP4" or "MP4V" => ("mpeg4", "MPEG-4 Visual", fourcc, null),
            "DIV3" => ("msmpeg4v3", "MPEG-4 Visual", "DivX 3", null),
            "H264" or "AVC1" or "X264" => ("h264", "AVC", null, null),
            "WVC1" => ("vc1", "VC-1", null, null),
            _ => (fourcc.ToLowerInvariant(), fourcc, null, null),
        };
    }

    /// <summary>mkvmerge's statistics of a track: BPS, DURATION, NUMBER_OF_FRAMES, NUMBER_OF_BYTES.</summary>
    private static void CitajZnacky(byte[] obsah, Dictionary<ulong, Dictionary<string, string>> statistiky)
    {
        foreach (var (id, tag) in Deti(obsah).Where(d => d.Id == 0x7373))
        {
            var stopy = new List<ulong>();
            var hodnoty = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var (cid, data) in Deti(tag))
            {
                if (cid == 0x63C0)
                {
                    stopy.AddRange(Deti(data).Where(d => d.Id == 0x63C5).Select(d => Cislo(d.Data)));
                }
                else if (cid == 0x67C8)
                {
                    string? meno = null, hodnota = null;
                    foreach (var (sid, sdata) in Deti(data))
                    {
                        if (sid == 0x45A3)
                        {
                            meno = Text(sdata);
                        }
                        else if (sid == 0x4487)
                        {
                            hodnota = Text(sdata);
                        }
                    }

                    if (meno is not null && hodnota is not null)
                    {
                        hodnoty[meno.ToUpperInvariant()] = hodnota;
                    }
                }
            }

            foreach (var uid in stopy)
            {
                if (!statistiky.TryGetValue(uid, out var znacky))
                {
                    statistiky[uid] = znacky = new Dictionary<string, string>(StringComparer.Ordinal);
                }

                foreach (var (k, v) in hodnoty)
                {
                    znacky[k] = v;
                }
            }
        }
    }

    private static void Statistiky(InfoStopy s, Dictionary<string, string> znacky)
    {
        if (znacky.TryGetValue("BPS", out var bps) && long.TryParse(bps, NumberStyles.None, CultureInfo.InvariantCulture, out var tok))
        {
            s.DatovyTok = tok;
        }

        if (znacky.TryGetValue("NUMBER_OF_BYTES", out var b) && long.TryParse(b, NumberStyles.None, CultureInfo.InvariantCulture, out var bajty))
        {
            s.VelkostStreamu = bajty;
        }

        if (znacky.TryGetValue("NUMBER_OF_FRAMES", out var f) && long.TryParse(f, NumberStyles.None, CultureInfo.InvariantCulture, out var snimky))
        {
            s.PocetSnimok = snimky;
        }

        if (znacky.TryGetValue("DURATION", out var d) && d.Length >= 8 &&
            TimeSpan.TryParse(d.Length > 16 ? d[..16] : d, CultureInfo.InvariantCulture, out var cas))
        {
            s.DlzkaSekund = cas.TotalSeconds;
        }

        // audio of constant rate (AC-3, E-AC-3, DTS core): frames of the same size
        if (s.Druh == DruhStopy.Zvuk && s.Kodek is "ac3" or "eac3" or "dts")
        {
            s.KonstantnyTok = true;
        }

        if (s.JeVideo && s.Snimky is null && s.PocetSnimok is > 0 && s.DlzkaSekund is > 0)
        {
            s.Snimky = s.PocetSnimok.Value / s.DlzkaSekund.Value;
        }
    }

    /// <summary>
    /// DTS-HD: the first frames of the DTS tracks in the first clusters (at most 4 MB): an extension substream with
    /// XLL is Master Audio, with XBR High Resolution Audio, with LBR Express.
    /// </summary>
    private static void DtsZBlokov(Stream subor, long cluster, long koniec, List<InfoStopy> stopy)
    {
        var dts = stopy.Where(s => s.Kodek == "dts").ToDictionary(s => s.Id);
        var dlzka = (int)Math.Min(4L << 20, koniec - cluster);
        if (dlzka <= 0)
        {
            return;
        }

        var data = CitajData(subor, cluster, dlzka);
        var pos = 0;
        var hotove = new HashSet<int>();
        while (pos < data.Length && hotove.Count < dts.Count)
        {
            if (!PrecitajHlavicku(data, pos, out var id, out var velkost, out var obsah))
            {
                return;
            }

            if (id == IdCluster)
            {
                pos = obsah; // into the cluster
                continue;
            }

            if (id is 0xA3 or 0xA1 && velkost is { } v && obsah + v <= data.Length)
            {
                var (stopa, dlzkaCisla) = Vint(data, obsah, oznacenie: false);
                if (dlzkaCisla > 0 && dts.TryGetValue((int)stopa, out var s) && hotove.Add((int)stopa))
                {
                    var ramec = data.AsSpan(obsah + dlzkaCisla + 3, (int)v - dlzkaCisla - 3);
                    DtsHd(ramec, s);
                }
            }

            if (id == 0xA0)
            {
                pos = obsah; // a block group: into it
                continue;
            }

            if (velkost is not { } dalej)
            {
                return;
            }

            pos = (int)Math.Min(data.Length, obsah + dalej);
        }
    }

    internal static void DtsHd(ReadOnlySpan<byte> ramec, InfoStopy s)
    {
        ReadOnlySpan<byte> exss = [0x64, 0x58, 0x20, 0x25];
        var i = ramec.IndexOf(exss);
        if (i < 0)
        {
            return;
        }

        var rozsirenie = ramec[i..];
        if (rozsirenie.IndexOf([(byte)0x41, (byte)0xA2, (byte)0x95, (byte)0x47]) >= 0)
        {
            (s.Kodek, s.Format, s.FormatInfo, s.ObchodnyNazov) = ("dts-hd ma", "DTS XLL", "DTS-HD Master Audio", "DTS-HD Master Audio");
        }
        else if (rozsirenie.IndexOf([(byte)0x65, (byte)0x5E, (byte)0x31, (byte)0x5E]) >= 0)
        {
            (s.Kodek, s.Format, s.FormatInfo, s.ObchodnyNazov) = ("dts-hd hra", "DTS XBR", "DTS-HD High Resolution Audio", "DTS-HD High Resolution Audio");
            s.KonstantnyTok = true;
        }
        else if (rozsirenie.IndexOf([(byte)0x0A, (byte)0x80, (byte)0x19, (byte)0x21]) >= 0)
        {
            (s.Format, s.FormatInfo, s.ObchodnyNazov) = ("DTS LBR", "DTS Express", "DTS Express");
        }

        if (s.Kodek == "dts-hd ma")
        {
            s.KonstantnyTok = false;
        }
    }

    // ---------------------------------------------------------------- EBML

    private readonly record struct Prvok(uint Id, long Data, long? Velkost);

    /// <summary>The header of an element at a position of the file; null past the end or for a broken one.</summary>
    private static Prvok? Hlavicka(Stream subor, long pozicia)
    {
        if (pozicia < 0 || pozicia >= subor.Length)
        {
            return null;
        }

        Span<byte> hlava = stackalloc byte[12];
        subor.Position = pozicia;
        var precitane = subor.ReadAtLeast(hlava, hlava.Length, throwOnEndOfStream: false);
        var pole = hlava[..precitane].ToArray();
        return PrecitajHlavicku(pole, 0, out var id, out var velkost, out var obsah) ? new Prvok(id, pozicia + obsah, velkost) : null;
    }

    private static bool PrecitajHlavicku(byte[] data, int pos, out uint id, out long? velkost, out int obsah)
    {
        id = 0;
        velkost = null;
        obsah = 0;
        var (idHodnota, idDlzka) = Vint(data, pos, oznacenie: true);
        if (idDlzka is 0 or > 4)
        {
            return false;
        }

        var (v, vDlzka) = Vint(data, pos + idDlzka, oznacenie: false);
        if (vDlzka == 0)
        {
            return false;
        }

        id = (uint)idHodnota;
        velkost = v == (1UL << (7 * vDlzka)) - 1 ? null : (long)v;
        obsah = pos + idDlzka + vDlzka;
        return velkost is null or >= 0;
    }

    /// <summary>An EBML variable-length number (with its marker for IDs); length 0 when broken.</summary>
    private static (ulong Hodnota, int Dlzka) Vint(byte[] data, int pos, bool oznacenie)
    {
        if (pos >= data.Length || data[pos] == 0)
        {
            return (0, 0);
        }

        var dlzka = System.Numerics.BitOperations.LeadingZeroCount((uint)data[pos]) - 24 + 1;
        if (pos + dlzka > data.Length)
        {
            return (0, 0);
        }

        ulong hodnota = oznacenie ? data[pos] : (ulong)(data[pos] & (0xFF >> dlzka));
        for (var i = 1; i < dlzka; i++)
        {
            hodnota = (hodnota << 8) | data[pos + i];
        }

        return (hodnota, dlzka);
    }

    /// <summary>The children of a master element read whole into memory.</summary>
    private static IEnumerable<(uint Id, byte[] Data)> Deti(byte[] obsah)
    {
        var pos = 0;
        while (pos < obsah.Length)
        {
            if (!PrecitajHlavicku(obsah, pos, out var id, out var velkost, out var data) || velkost is not { } v || data + v > obsah.Length)
            {
                yield break;
            }

            yield return (id, obsah.AsSpan(data, (int)v).ToArray());
            pos = data + (int)v;
        }
    }

    private static byte[] CitajData(Stream subor, long pozicia, long? velkost)
    {
        var dlzka = (int)Math.Max(0, Math.Min(velkost ?? 0, Math.Min(NajviacPrvok, subor.Length - pozicia)));
        var data = new byte[dlzka];
        subor.Position = pozicia;
        subor.ReadExactly(data);
        return data;
    }

    private static ulong Cislo(byte[] data)
    {
        ulong v = 0;
        foreach (var b in data.AsSpan(0, Math.Min(8, data.Length)))
        {
            v = (v << 8) | b;
        }

        return v;
    }

    private static double? Desatinne(byte[] data) => data.Length switch
    {
        4 => BinaryPrimitives.ReadSingleBigEndian(data),
        8 => BinaryPrimitives.ReadDoubleBigEndian(data),
        _ => null,
    };

    private static string? Text(byte[] data)
    {
        var koniec = Array.IndexOf(data, (byte)0);
        var text = Encoding.UTF8.GetString(data, 0, koniec < 0 ? data.Length : koniec).Trim();
        return text.Length > 0 ? text : null;
    }
}
