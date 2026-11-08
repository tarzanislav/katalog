using System.Globalization;
using Katalog.Jadro.Model;

namespace Katalog.Media;

/// <summary>Reads bits of a codec's configuration (MSB first), with Exp-Golomb numbers; past the end it gives zeros.</summary>
internal sealed class CitacBitov(byte[] data)
{
    private int _bit;

    public bool Koniec => _bit >= data.Length * 8;

    public int Bit()
    {
        if (_bit >= data.Length * 8)
        {
            _bit++;
            return 0;
        }

        var b = (data[_bit >> 3] >> (7 - (_bit & 7))) & 1;
        _bit++;
        return b;
    }

    public long Bity(int pocet)
    {
        long v = 0;
        for (var i = 0; i < pocet; i++)
        {
            v = (v << 1) | (long)Bit();
        }

        return v;
    }

    public void Preskoc(int pocet) => _bit += pocet;

    /// <summary>ue(v); a broken stream (over 31 leading zeros) gives -1.</summary>
    public long Ue()
    {
        var nuly = 0;
        while (Bit() == 0)
        {
            if (++nuly > 31 || Koniec)
            {
                return -1;
            }
        }

        return (1L << nuly) - 1 + Bity(nuly);
    }

    public long Se()
    {
        var k = Ue();
        return k <= 0 ? 0 : (k & 1) == 1 ? (k + 1) / 2 : -(k / 2);
    }

    /// <summary>A NAL unit without its emulation prevention bytes (00 00 03 → 00 00).</summary>
    public static byte[] BezEmulacie(ReadOnlySpan<byte> nal)
    {
        var vysledok = new List<byte>(nal.Length);
        var nuly = 0;
        foreach (var b in nal)
        {
            if (nuly >= 2 && b == 3)
            {
                nuly = 0;
                continue;
            }

            vysledok.Add(b);
            nuly = b == 0 ? nuly + 1 : 0;
        }

        return [.. vysledok];
    }
}

/// <summary>
/// What the configuration records of video codecs say (hvcC, avcC, av1C as Matroska's CodecPrivate or MP4's boxes):
/// profile and level, chroma subsampling, bit depth, and from the HEVC sequence parameter set the colours (HDR).
/// </summary>
internal static class KodekyVidea
{
    /// <summary>HEVCDecoderConfigurationRecord: "Main 10@L4@Main", 4:2:0, 10 bits; the colours from its SPS.</summary>
    public static void Hevc(ReadOnlySpan<byte> hvcc, InfoStopy stopa)
    {
        if (hvcc.Length < 23)
        {
            return;
        }

        var profil = hvcc[1] & 0x1F;
        var tier = (hvcc[1] >> 5) & 1;
        var level = hvcc[12];
        stopa.Profil = $"{ProfilHevc(profil)}@L{Level(level / 30.0)}@{(tier == 1 ? "High" : "Main")}";
        stopa.Podvzorkovanie = Chroma(hvcc[16] & 3);
        stopa.BitovaHlbka = (hvcc[17] & 7) + 8;
        stopa.FarebnyPriestor = "YUV";
        stopa.DlzkaNal = (hvcc[21] & 3) + 1;

        // arrays of parameter sets: the SPS (type 33) carries the VUI with the colours
        var pocet = hvcc[22];
        var i = 23;
        for (var a = 0; a < pocet && i + 3 <= hvcc.Length; a++)
        {
            var typ = hvcc[i] & 0x3F;
            var nalov = (hvcc[i + 1] << 8) | hvcc[i + 2];
            i += 3;
            for (var n = 0; n < nalov && i + 2 <= hvcc.Length; n++)
            {
                var dlzka = (hvcc[i] << 8) | hvcc[i + 1];
                i += 2;
                if (i + dlzka > hvcc.Length)
                {
                    return;
                }

                if (typ == 33)
                {
                    SpsHevc(hvcc.Slice(i, dlzka), stopa);
                }

                i += dlzka;
            }
        }
    }

    private static string ProfilHevc(int profil) => profil switch
    {
        1 => "Main",
        2 => "Main 10",
        3 => "Main Still",
        4 => "Format Range",
        5 => "High Throughput",
        9 => "Screen Content",
        _ => "Profil " + profil.ToString(CultureInfo.InvariantCulture),
    };

    /// <summary>The colours of an HEVC sequence parameter set (its VUI); a stream it cannot follow is left as it is.</summary>
    internal static void SpsHevc(ReadOnlySpan<byte> nal, InfoStopy stopa)
    {
        if (nal.Length < 4)
        {
            return;
        }

        var r = new CitacBitov(CitacBitov.BezEmulacie(nal[2..]));
        r.Preskoc(4); // sps_video_parameter_set_id
        var maxSub = (int)r.Bity(3);
        r.Preskoc(1);
        ProfilTierLevel(r, maxSub);
        r.Ue(); // sps_seq_parameter_set_id
        var chroma = r.Ue();
        if (chroma == 3)
        {
            r.Preskoc(1);
        }

        if (chroma is >= 0 and <= 3)
        {
            stopa.Podvzorkovanie = Chroma((int)chroma);
        }

        r.Ue();
        r.Ue(); // width, height
        if (r.Bit() == 1)
        {
            r.Ue();
            r.Ue();
            r.Ue();
            r.Ue();
        }

        var hlbka = r.Ue();
        if (hlbka is >= 0 and <= 8)
        {
            stopa.BitovaHlbka = (int)hlbka + 8;
        }

        r.Ue(); // bit_depth_chroma
        var log2Poc = r.Ue() + 4;
        var poradie = r.Bit();
        for (var i = poradie == 1 ? 0 : maxSub; i <= maxSub; i++)
        {
            r.Ue();
            r.Ue();
            r.Ue();
        }

        r.Ue();
        r.Ue();
        r.Ue();
        r.Ue();
        r.Ue();
        r.Ue();
        if (r.Bit() == 1 && r.Bit() == 1)
        {
            ScalingList(r);
        }

        r.Preskoc(2); // amp, sample_adaptive_offset
        if (r.Bit() == 1)
        {
            r.Preskoc(8);
            r.Ue();
            r.Ue();
            r.Preskoc(1);
        }

        var sady = r.Ue();
        if (sady is < 0 or > 64)
        {
            return;
        }

        var obrazkov = new int[sady];
        for (var i = 0; i < sady; i++)
        {
            obrazkov[i] = StRefPicSet(r, i, (int)sady, obrazkov);
            if (obrazkov[i] < 0)
            {
                return;
            }
        }

        if (r.Bit() == 1)
        {
            var dlhe = r.Ue();
            for (var i = 0; i < dlhe && !r.Koniec; i++)
            {
                r.Preskoc((int)log2Poc);
                r.Preskoc(1);
            }
        }

        r.Preskoc(2); // temporal_mvp, strong_intra_smoothing
        if (r.Bit() == 0)
        {
            return;
        }

        // VUI
        if (r.Bit() == 1 && r.Bity(8) == 255)
        {
            r.Preskoc(32);
        }

        if (r.Bit() == 1)
        {
            r.Preskoc(1);
        }

        if (r.Bit() == 1)
        {
            r.Preskoc(3);
            stopa.FarebnyRozsah = r.Bit() == 1 ? "Full" : "Limited";
            if (r.Bit() == 1)
            {
                Farby(stopa, (int)r.Bity(8), (int)r.Bity(8), (int)r.Bity(8));
            }
        }
    }

    private static void ProfilTierLevel(CitacBitov r, int maxSub)
    {
        r.Preskoc(96); // general profile space … general_level_idc
        var profil = new bool[maxSub];
        var level = new bool[maxSub];
        for (var i = 0; i < maxSub; i++)
        {
            profil[i] = r.Bit() == 1;
            level[i] = r.Bit() == 1;
        }

        if (maxSub > 0)
        {
            r.Preskoc(2 * (8 - maxSub));
        }

        for (var i = 0; i < maxSub; i++)
        {
            r.Preskoc((profil[i] ? 88 : 0) + (level[i] ? 8 : 0));
        }
    }

    private static void ScalingList(CitacBitov r)
    {
        for (var velkost = 0; velkost < 4; velkost++)
        {
            for (var matica = 0; matica < 6; matica += velkost == 3 ? 3 : 1)
            {
                if (r.Bit() == 0)
                {
                    r.Ue();
                }
                else
                {
                    var koef = Math.Min(64, 1 << (4 + (velkost << 1)));
                    if (velkost > 1)
                    {
                        r.Se();
                    }

                    for (var i = 0; i < koef; i++)
                    {
                        r.Se();
                    }
                }
            }
        }
    }

    /// <summary>st_ref_pic_set: returns the number of pictures it names (needed by the next sets), -1 for a broken one.</summary>
    private static int StRefPicSet(CitacBitov r, int index, int sady, int[] obrazkov)
    {
        if (index != 0 && r.Bit() == 1)
        {
            var delta = index == sady ? r.Ue() + 1 : 1;
            if (delta < 1 || delta > index)
            {
                return -1;
            }

            r.Preskoc(1);
            r.Ue();
            var pocet = 0;
            for (var j = 0; j <= obrazkov[index - delta]; j++)
            {
                var pouzity = r.Bit();
                if (pouzity == 1 || r.Bit() == 1)
                {
                    pocet++;
                }
            }

            return pocet;
        }

        var negativne = r.Ue();
        var pozitivne = r.Ue();
        if (negativne is < 0 or > 16 || pozitivne is < 0 or > 16)
        {
            return -1;
        }

        for (var i = 0; i < negativne + pozitivne; i++)
        {
            r.Ue();
            r.Preskoc(1);
        }

        return (int)(negativne + pozitivne);
    }

    /// <summary>AVCDecoderConfigurationRecord: "High@L4.1", and for High profiles chroma and bit depth.</summary>
    public static void Avc(ReadOnlySpan<byte> avcc, InfoStopy stopa)
    {
        if (avcc.Length < 7)
        {
            return;
        }

        var profil = avcc[1];
        stopa.Profil = $"{ProfilAvc(profil)}@L{Level(avcc[3] / 10.0)}";
        stopa.FarebnyPriestor = "YUV";
        stopa.Podvzorkovanie = "4:2:0";
        stopa.BitovaHlbka = 8;
        stopa.DlzkaNal = (avcc[4] & 3) + 1;

        // past the SPS and PPS, High profiles add chroma_format and bit depths
        var i = 6;
        var sps = avcc[5] & 0x1F;
        for (var n = 0; n < sps && i + 2 <= avcc.Length; n++)
        {
            i += 2 + ((avcc[i] << 8) | avcc[i + 1]);
        }

        if (i >= avcc.Length)
        {
            return;
        }

        var pps = avcc[i++];
        for (var n = 0; n < pps && i + 2 <= avcc.Length; n++)
        {
            i += 2 + ((avcc[i] << 8) | avcc[i + 1]);
        }

        if (profil is 100 or 110 or 122 or 144 or 244 && i + 3 <= avcc.Length)
        {
            stopa.Podvzorkovanie = Chroma(avcc[i] & 3);
            stopa.BitovaHlbka = (avcc[i + 1] & 7) + 8;
        }
    }

    private static string ProfilAvc(int profil) => profil switch
    {
        66 => "Baseline",
        77 => "Main",
        88 => "Extended",
        100 => "High",
        110 => "High 10",
        122 => "High 4:2:2",
        244 => "High 4:4:4 Predictive",
        _ => "Profil " + profil.ToString(CultureInfo.InvariantCulture),
    };

    /// <summary>AV1CodecConfigurationRecord: "Main@L4.0", bit depth and chroma.</summary>
    public static void Av1(ReadOnlySpan<byte> av1c, InfoStopy stopa)
    {
        if (av1c.Length < 4)
        {
            return;
        }

        var profil = av1c[1] >> 5;
        var level = av1c[1] & 0x1F;
        var vysoka = (av1c[2] >> 6) & 1;
        var dvanast = (av1c[2] >> 5) & 1;
        var mono = (av1c[2] >> 4) & 1;
        var x = (av1c[2] >> 3) & 1;
        var y = (av1c[2] >> 2) & 1;
        stopa.Profil = (profil switch { 0 => "Main", 1 => "High", _ => "Professional" }) +
                       $"@L{(2 + (level >> 2)).ToString(CultureInfo.InvariantCulture)}.{(level & 3).ToString(CultureInfo.InvariantCulture)}";
        stopa.BitovaHlbka = vysoka == 1 ? dvanast == 1 ? 12 : 10 : 8;
        stopa.FarebnyPriestor = "YUV";
        stopa.Podvzorkovanie = mono == 1 ? "4:0:0" : x == 1 && y == 1 ? "4:2:0" : x == 1 ? "4:2:2" : "4:4:4";
    }

    /// <summary>The audio object type of an AAC AudioSpecificConfig: LC, HE-AAC (SBR), HE-AACv2 (PS), xHE-AAC (USAC).</summary>
    public static string? ProfilAac(ReadOnlySpan<byte> asc)
    {
        if (asc.Length < 1)
        {
            return null;
        }

        var typ = asc[0] >> 3;
        if (typ == 31 && asc.Length >= 2)
        {
            typ = 32 + (((asc[0] & 7) << 3) | (asc[1] >> 5)); // the escape of the object type
        }

        return typ switch
        {
            1 => "Main",
            2 => "LC",
            3 => "SSR",
            4 => "LTP",
            5 => "HE-AAC",
            29 => "HE-AACv2",
            39 => "ER AAC ELD",
            42 => "xHE-AAC",
            _ => typ.ToString(CultureInfo.InvariantCulture),
        };
    }

    /// <summary>The profile of Apple ProRes by its four-character code.</summary>
    public static string ProfilProRes(string fourcc) => fourcc switch
    {
        "apco" => "422 Proxy",
        "apcs" => "422 LT",
        "apcn" => "422",
        "apch" => "422 HQ",
        "ap4h" => "4444",
        "ap4x" => "4444 XQ",
        _ => fourcc,
    };

    /// <summary>VPCodecConfigurationRecord (vpcC, MP4): profile, level, bit depth, chroma and colours of VP9.</summary>
    public static void Vp9(ReadOnlySpan<byte> vpcc, InfoStopy stopa)
    {
        if (vpcc.Length < 10)
        {
            return;
        }

        stopa.Profil = $"Profil {vpcc[4]}@L{(vpcc[5] / 10.0).ToString("0.#", CultureInfo.InvariantCulture)}";
        stopa.BitovaHlbka = vpcc[6] >> 4;
        stopa.FarebnyPriestor = "YUV";
        stopa.Podvzorkovanie = ((vpcc[6] >> 1) & 7) switch { 0 or 1 => "4:2:0", 2 => "4:2:2", _ => "4:4:4" };
        stopa.FarebnyRozsah = (vpcc[6] & 1) == 1 ? "Full" : "Limited";
        Farby(stopa, vpcc[7], vpcc[8], vpcc[9]);
    }

    /// <summary>Colour primaries, transfer and matrix by ISO/IEC 23091-2 (H.273); HDR10 and HLG from the transfer.</summary>
    public static void Farby(InfoStopy stopa, int primarne, int prenos, int matica)
    {
        stopa.PrimarneFarby = Primarne(primarne) ?? stopa.PrimarneFarby;
        stopa.Prenos = Prenos(prenos) ?? stopa.Prenos;
        stopa.Matica = Matica(matica) ?? stopa.Matica;
        if (stopa.Hdr == Hdr.Ziadne)
        {
            stopa.Hdr = prenos switch { 16 => Hdr.Hdr10, 18 => Hdr.Hlg, _ => Hdr.Ziadne };
        }
    }

    public static string? Primarne(int kod) => kod switch
    {
        1 => "BT.709",
        4 => "BT.470 M",
        5 => "BT.601 PAL",
        6 => "BT.601 NTSC",
        7 => "SMPTE 240M",
        9 => "BT.2020",
        11 => "DCI P3",
        12 => "Display P3",
        _ => null,
    };

    public static string? Prenos(int kod) => kod switch
    {
        1 or 6 or 14 or 15 => kod switch { 1 => "BT.709", 6 => "BT.601", 14 => "BT.2020 (10-bit)", _ => "BT.2020 (12-bit)" },
        4 => "BT.470 M",
        5 => "BT.470 BG",
        7 => "SMPTE 240M",
        8 => "lineárne",
        13 => "sRGB",
        16 => "PQ (SMPTE ST 2084)",
        18 => "HLG",
        _ => null,
    };

    public static string? Matica(int kod) => kod switch
    {
        0 => "Identity",
        1 => "BT.709",
        5 or 6 => "BT.601",
        7 => "SMPTE 240M",
        9 => "BT.2020 non-constant",
        10 => "BT.2020 constant",
        14 => "ICtCp",
        _ => null,
    };

    private static string Chroma(int format) => format switch { 0 => "4:0:0", 1 => "4:2:0", 2 => "4:2:2", _ => "4:4:4" };

    /// <summary>4 → "4", 4.1 → "4.1".</summary>
    private static string Level(double level) => Math.Round(level, 1).ToString("0.#", CultureInfo.InvariantCulture);
}
