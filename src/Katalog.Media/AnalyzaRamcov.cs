using System.Globalization;
using Katalog.Jadro.Model;

namespace Katalog.Media;

/// <summary>
/// What the first frame of a track tells beyond the container: Dolby Atmos in TrueHD and in E-AC-3 (JOC), the
/// dialogue level and channel layout of AC-3 and E-AC-3, DTS-HD MA/HRA/Express and DTS:X, and in the first picture of
/// HEVC the metadata of HDR10+ and Dolby Vision. Each reads only the bytes it is given and never throws.
/// </summary>
public static class AnalyzaRamcov
{
    /// <summary>The audio frame of a track by its codec (as KATALOG names it).</summary>
    public static void Zvuk(ReadOnlySpan<byte> ramec, InfoStopy s)
    {
        switch (s.Kodek)
        {
            case "ac3" or "eac3":
                DolbyDigital(ramec, s);
                break;
            case "truehd":
                TrueHd(ramec, s);
                break;
            case "dts" or "dts-hd ma" or "dts-hd hra":
                Dts(ramec, s);
                break;
        }
    }

    // ---------------------------------------------------------------- Dolby Digital (AC-3) and Dolby Digital Plus (E-AC-3)

    private static readonly int[] KanalyAcmod = [2, 1, 2, 3, 3, 4, 4, 5];

    private static readonly string[] RozlozenieAcmod =
        ["L R", "C", "L R", "L R C", "L R S", "L R C S", "L R Ls Rs", "L R C Ls Rs"];

    /// <summary>The bit rates of AC-3 by frmsizecod / 2 (kb/s).</summary>
    private static readonly int[] TokyAc3 = [32, 40, 48, 56, 64, 80, 96, 112, 128, 160, 192, 224, 256, 320, 384, 448, 512, 576, 640];

    /// <summary>
    /// The first syncframe of AC-3 or E-AC-3 (sync 0x0B77): channels and their layout, the dialogue level, the data rate
    /// of AC-3, and in E-AC-3 the flag of the extension type A (joint object coding – Dolby Atmos).
    /// </summary>
    internal static void DolbyDigital(ReadOnlySpan<byte> data, InfoStopy s)
    {
        var i = Najdi(data, [0x0B, 0x77]);
        if (i < 0 || data.Length - i < 8)
        {
            return;
        }

        var bsid = data[i + 5] >> 3;
        var r = new CitacBitov(data[(i + 2)..].ToArray());
        int acmod, lfe, dialnorm;
        var atmos = false;
        if (bsid <= 10)
        {
            // AC-3: crc1, fscod, frmsizecod, bsid, bsmod, acmod
            r.Preskoc(16);
            var fscod = (int)r.Bity(2);
            var frmsizecod = (int)r.Bity(6);
            r.Preskoc(5 + 3);
            acmod = (int)r.Bity(3);
            if ((acmod & 1) == 1 && acmod != 1)
            {
                r.Preskoc(2); // cmixlev
            }

            if ((acmod & 4) == 4)
            {
                r.Preskoc(2); // surmixlev
            }

            if (acmod == 2)
            {
                r.Preskoc(2); // dsurmod
            }

            lfe = r.Bit();
            dialnorm = (int)r.Bity(5);
            s.Vzorkovanie ??= fscod switch { 0 => 48000, 1 => 44100, 2 => 32000, _ => null };
            if (fscod != 3 && frmsizecod / 2 < TokyAc3.Length)
            {
                s.DatovyTok = TokyAc3[frmsizecod / 2] * 1000L;
                s.KonstantnyTok = true;
            }
        }
        else if (bsid is >= 11 and <= 16)
        {
            // E-AC-3: strmtyp, substreamid, frmsiz, fscod, numblkscod, acmod, lfeon, bsid, dialnorm, compre …
            var strmtyp = (int)r.Bity(2);
            r.Preskoc(3);
            var velkostRamca = ((int)r.Bity(11) + 1) * 2;
            var fscod = (int)r.Bity(2);
            var bloky = 6;
            var frekvencia = fscod switch { 0 => 48000, 1 => 44100, 2 => 32000, _ => 0 };
            if (fscod == 3)
            {
                frekvencia = (int)r.Bity(2) switch { 0 => 24000, 1 => 22050, 2 => 16000, _ => 0 };
            }
            else
            {
                bloky = (int)r.Bity(2) switch { 0 => 1, 1 => 2, 2 => 3, _ => 6 };
            }

            // E-AC-3 of a constant size: the rate of the independent substream from its frame size
            if (frekvencia > 0 && strmtyp != 1)
            {
                s.DatovyTok = (long)Math.Round(velkostRamca * 8.0 * frekvencia / (256.0 * bloky));
                s.KonstantnyTok = true;
                s.OdhadToku = false;
            }

            acmod = (int)r.Bity(3);
            lfe = r.Bit();
            r.Preskoc(5);
            dialnorm = (int)r.Bity(5);
            if (frekvencia > 0)
            {
                s.Vzorkovanie ??= frekvencia;
            }
            if (r.Bit() == 1)
            {
                r.Preskoc(8);
            }

            if (acmod == 0)
            {
                r.Preskoc(5);
                if (r.Bit() == 1)
                {
                    r.Preskoc(8);
                }
            }

            if (strmtyp == 1 && r.Bit() == 1)
            {
                r.Preskoc(16); // chanmap of a dependent substream
            }

            MiesanieEac3(r, acmod, lfe, strmtyp, bloky);
            InformacieEac3(r, acmod, fscod);
            if (strmtyp == 0 && bloky != 6)
            {
                r.Preskoc(1); // convsync
            }

            if (strmtyp == 2 && (bloky == 6 || r.Bit() == 1))
            {
                r.Preskoc(6); // frmsizecod of a converted AC-3
            }

            if (r.Bit() == 1)
            {
                r.Preskoc(6); // addbsil
                r.Preskoc(7);
                atmos = r.Bit() == 1; // flag_ec3_extension_type_a: joint object coding
            }
        }
        else
        {
            return;
        }

        s.Kanaly ??= KanalyAcmod[acmod] + lfe;
        s.RozlozenieKanalov ??= Rozlozenie(acmod, lfe == 1);
        s.Dialogy = dialnorm == 0 ? -31 : -dialnorm;
        if (atmos)
        {
            s.Atmos = true;
            s.ObchodnyNazov = "Dolby Digital Plus s Dolby Atmos";
            s.FormatInfo = "Enhanced AC-3 s JOC (Joint Object Coding)";
        }
    }

    /// <summary>"L R C LFE Ls Rs": LFE after the centre, else after the front pair.</summary>
    internal static string Rozlozenie(int acmod, bool lfe)
    {
        var zaklad = RozlozenieAcmod[acmod];
        if (!lfe)
        {
            return zaklad;
        }

        return zaklad == "C" ? "C LFE"
            : zaklad.StartsWith("L R C", StringComparison.Ordinal) ? "L R C LFE" + zaklad[5..]
            : "L R LFE" + zaklad[3..];
    }

    /// <summary>Skips the mixing metadata of an E-AC-3 frame (mixmdate and what follows).</summary>
    private static void MiesanieEac3(CitacBitov r, int acmod, int lfe, int strmtyp, int bloky)
    {
        if (r.Bit() == 0)
        {
            return;
        }

        if (acmod > 2)
        {
            r.Preskoc(2);
        }

        if ((acmod & 1) == 1 && acmod > 2)
        {
            r.Preskoc(6);
        }

        if ((acmod & 4) == 4)
        {
            r.Preskoc(6);
        }

        if (lfe == 1 && r.Bit() == 1)
        {
            r.Preskoc(5);
        }

        if (strmtyp != 0)
        {
            return;
        }

        for (var i = 0; i < (acmod == 0 ? 2 : 1); i++)
        {
            if (r.Bit() == 1)
            {
                r.Preskoc(6); // pgmscl
            }
        }

        if (r.Bit() == 1)
        {
            r.Preskoc(6); // extpgmscl
        }

        switch (r.Bity(2))
        {
            case 1:
                r.Preskoc(5);
                break;
            case 2:
                r.Preskoc(12);
                break;
            case 3:
                r.Preskoc(((int)r.Bity(5) + 2) * 8);
                break;
        }

        if (acmod < 2)
        {
            for (var i = 0; i < (acmod == 0 ? 2 : 1); i++)
            {
                if (r.Bit() == 1)
                {
                    r.Preskoc(14); // panmean, paninfo
                }
            }
        }

        if (r.Bit() == 1)
        {
            for (var b = 0; b < bloky; b++)
            {
                if (bloky == 1 || r.Bit() == 1)
                {
                    r.Preskoc(5);
                }
            }
        }
    }

    /// <summary>Skips the informational metadata of an E-AC-3 frame (infomdate and what follows).</summary>
    private static void InformacieEac3(CitacBitov r, int acmod, int fscod)
    {
        if (r.Bit() == 0)
        {
            return;
        }

        r.Preskoc(3 + 2);
        if (acmod == 2)
        {
            r.Preskoc(4);
        }

        if (acmod >= 6)
        {
            r.Preskoc(2);
        }

        for (var i = 0; i < (acmod == 0 ? 2 : 1); i++)
        {
            if (r.Bit() == 1)
            {
                r.Preskoc(8);
            }
        }

        if (fscod < 3)
        {
            r.Preskoc(1);
        }
    }

    // ---------------------------------------------------------------- Dolby TrueHD

    /// <summary>
    /// The major sync of TrueHD (0xF8726FBA, signature 0xB752): four substreams with the 16-channel presentation are
    /// Dolby Atmos; the channels of the 8-channel presentation give the layout.
    /// </summary>
    internal static void TrueHd(ReadOnlySpan<byte> data, InfoStopy s)
    {
        var i = Najdi(data, [0xF8, 0x72, 0x6F, 0xBA]);
        if (i < 0 || data.Length - i < 18 || data[i + 8] != 0xB7 || data[i + 9] != 0x52)
        {
            return;
        }

        // format info: sampling rate, then the 6- and 8-channel presentations
        var frekvencia = data[i + 4] >> 4;
        s.Vzorkovanie ??= frekvencia switch { 0 => 48000, 1 => 96000, 2 => 192000, 8 => 44100, 9 => 88200, 10 => 176400, _ => null };
        var r = new CitacBitov(data.Slice(i + 5, 3).ToArray());
        r.Preskoc(2 + 2);
        var sest = (int)r.Bity(5);
        r.Preskoc(2);
        var osem = (int)r.Bity(13);
        var kanaly = KanalyTrueHd(osem != 0 ? osem : sest, osem != 0 ? 13 : 5);
        if (kanaly > 0)
        {
            s.Kanaly ??= kanaly;
        }

        var substreamov = data[i + 16] >> 4;
        var info = data[i + 17];
        if (substreamov == 4 && (info & 0x80) != 0)
        {
            s.Atmos = true;
            s.ObchodnyNazov = "Dolby TrueHD s Dolby Atmos";
            s.FormatInfo = "Meridian Lossless Packing FBA s 16-kanálovou prezentáciou";
        }

        // the bit rate is variable: lossless
        s.KonstantnyTok = false;
    }

    /// <summary>The channels of a TrueHD channel assignment (pairs and single speakers, LFE included).</summary>
    private static int KanalyTrueHd(int priradenie, int bitov)
    {
        int[] pocty = [2, 1, 1, 2, 2, 2, 2, 1, 1, 2, 2, 1, 1];
        var sucet = 0;
        for (var b = 0; b < bitov; b++)
        {
            if ((priradenie & (1 << b)) != 0)
            {
                sucet += pocty[b];
            }
        }

        return sucet;
    }

    // ---------------------------------------------------------------- DTS

    private static readonly int[] KanalyDts = [1, 2, 2, 2, 2, 3, 3, 4, 4, 5, 6, 6, 6, 7, 8, 8];

    private static readonly int[] FrekvencieDts = [0, 8000, 16000, 32000, 0, 0, 11025, 22050, 44100, 0, 0, 12000, 24000, 48000, 0, 0];

    private static readonly int[] TokyDts =
        [32, 56, 64, 96, 112, 128, 192, 224, 256, 320, 384, 448, 512, 576, 640, 768, 960, 1024, 1152, 1280, 1344, 1408, 1411, 1472, 1536];

    private static readonly int[] FrekvencieExss = [8000, 16000, 32000, 64000, 128000, 22050, 44100, 88200, 176400, 352800, 12000, 24000, 48000, 96000, 192000, 384000];

    /// <summary>The DTS core frame (sync 7FFE8001): channels, LFE, sample rate, bit rate, DTS-ES.</summary>
    private static void JadroDts(ReadOnlySpan<byte> data, InfoStopy s)
    {
        var i = Najdi(data, [0x7F, 0xFE, 0x80, 0x01]);
        if (i < 0 || data.Length - i < 15)
        {
            return;
        }

        var r = new CitacBitov(data.Slice(i + 4, 11).ToArray());
        r.Preskoc(1 + 5 + 1 + 7 + 14);
        var amode = (int)r.Bity(6);
        var frekvencia = (int)r.Bity(4);
        var tok = (int)r.Bity(5);
        r.Preskoc(5);
        var rozsirenie = (int)r.Bity(3);
        var maRozsirenie = r.Bit() == 1;
        r.Preskoc(1);
        var lfe = (int)r.Bity(2);
        if (amode < KanalyDts.Length)
        {
            s.Kanaly ??= KanalyDts[amode] + (lfe is 1 or 2 ? 1 : 0);
        }

        if (FrekvencieDts[frekvencia] > 0)
        {
            s.Vzorkovanie ??= FrekvencieDts[frekvencia];
        }

        if (tok < TokyDts.Length && s.Kodek == "dts")
        {
            s.DatovyTok ??= TokyDts[tok] * 1000L;
            s.KonstantnyTok ??= true;
        }

        if (maRozsirenie && rozsirenie is 0 or 6 && s.Kodek == "dts")
        {
            (s.Format, s.ObchodnyNazov) = ("DTS ES", "DTS-ES");
        }
    }

    /// <summary>
    /// The header of the extension substream and its first asset: the full number of channels, bit depth and sample rate
    /// of DTS-HD (the core says only 5.1 / 48 kHz of a 7.1 / 24-bit Master Audio).
    /// </summary>
    private static void HlavickaExss(ReadOnlySpan<byte> exss, InfoStopy s)
    {
        var r = new CitacBitov(exss[4..Math.Min(exss.Length, 600)].ToArray());
        r.Preskoc(8);
        var index = (int)r.Bity(2);
        var dlhaHlavicka = r.Bit() == 1;
        r.Preskoc(dlhaHlavicka ? 12 : 8);
        r.Preskoc(dlhaHlavicka ? 20 : 16);
        if (r.Bit() == 0)
        {
            return; // without static fields the asset says nothing more
        }

        r.Preskoc(2 + 3);
        if (r.Bit() == 1)
        {
            r.Preskoc(36);
        }

        var prezentacii = (int)r.Bity(3) + 1;
        var aktiv = (int)r.Bity(3) + 1;
        var masky = new int[prezentacii];
        for (var p = 0; p < prezentacii; p++)
        {
            masky[p] = (int)r.Bity(index + 1);
        }

        for (var p = 0; p < prezentacii; p++)
        {
            for (var ss = 0; ss <= index; ss++)
            {
                if ((masky[p] & (1 << ss)) != 0)
                {
                    r.Preskoc(8);
                }
            }
        }

        if (r.Bit() == 1)
        {
            r.Preskoc(2);
            var bitovMasky = ((int)r.Bity(2) + 1) << 2;
            var konfiguracii = (int)r.Bity(2) + 1;
            r.Preskoc(bitovMasky * konfiguracii);
        }

        for (var a = 0; a < aktiv; a++)
        {
            r.Preskoc(dlhaHlavicka ? 20 : 16);
        }

        // the first asset descriptor
        r.Preskoc(9 + 3);
        if (r.Bit() == 1)
        {
            r.Preskoc(4);
        }

        if (r.Bit() == 1)
        {
            r.Preskoc(24);
        }

        if (r.Bit() == 1)
        {
            r.Preskoc(((int)r.Bity(10) + 1) * 8);
        }

        var bity = (int)r.Bity(5) + 1;
        var frekvencia = (int)r.Bity(4);
        var kanaly = (int)r.Bity(8) + 1;
        if (r.Koniec || kanaly > 32 || bity is < 8 or > 32)
        {
            return;
        }

        s.Kanaly = kanaly;
        s.RozlozenieKanalov = null;
        s.BitovaHlbka = bity;
        s.Vzorkovanie = FrekvencieExss[frekvencia];
    }

    /// <summary>
    /// DTS: the extension substream (0x64582025) with XLL is Master Audio, with XBR High Resolution, with LBR Express;
    /// DTS:X and IMAX Enhanced are marked inside XLL. The core alone may carry 96/24 (X96).
    /// </summary>
    internal static void Dts(ReadOnlySpan<byte> ramec, InfoStopy s)
    {
        if (Najdi(ramec, [0x1D, 0x95, 0xF2, 0x62]) >= 0)
        {
            s.Format = "DTS 96/24";
        }

        JadroDts(ramec, s);
        var i = Najdi(ramec, [0x64, 0x58, 0x20, 0x25]);
        if (i < 0)
        {
            return;
        }

        var rozsirenie = ramec[i..];
        if (rozsirenie.Length > 16)
        {
            HlavickaExss(rozsirenie, s);
        }

        if (Najdi(rozsirenie, [0x41, 0xA2, 0x95, 0x47]) >= 0)
        {
            var x = Najdi(rozsirenie, [0x02, 0x00, 0x08, 0x50]) >= 0;
            var imax = Najdi(rozsirenie, [0xF1, 0x40, 0x00, 0xD0]) >= 0;
            (s.Kodek, s.Format, s.FormatInfo, s.ObchodnyNazov) = imax
                ? ("dts:x", "DTS XLL X", "DTS-HD Master Audio s DTS:X", "DTS:X IMAX Enhanced")
                : x
                    ? ("dts:x", "DTS XLL X", "DTS-HD Master Audio s DTS:X", "DTS:X")
                    : ("dts-hd ma", "DTS XLL", "DTS-HD Master Audio", "DTS-HD Master Audio");
            s.KonstantnyTok = false;
        }
        else if (Najdi(rozsirenie, [0x65, 0x5E, 0x31, 0x5E]) >= 0)
        {
            (s.Kodek, s.Format, s.FormatInfo, s.ObchodnyNazov) = ("dts-hd hra", "DTS XBR", "DTS-HD High Resolution Audio", "DTS-HD High Resolution Audio");
            s.KonstantnyTok = true;
        }
        else if (Najdi(rozsirenie, [0x0A, 0x80, 0x19, 0x21]) >= 0)
        {
            (s.Format, s.FormatInfo, s.ObchodnyNazov) = ("DTS LBR", "DTS Express", "DTS Express");
        }
    }

    // ---------------------------------------------------------------- HEVC: HDR10+ and Dolby Vision in the picture

    /// <summary>
    /// The first picture of HEVC (NAL units with a length of <paramref name="dlzkaDlzky"/> bytes): an SEI with the
    /// ITU-T T.35 metadata of Samsung (HDR10+, ST 2094-40) and an RPU of Dolby Vision (NAL type 62).
    /// </summary>
    public static void ObrazHevc(ReadOnlySpan<byte> ramec, int dlzkaDlzky, InfoStopy s)
    {
        if (dlzkaDlzky is < 1 or > 4)
        {
            return;
        }

        var pos = 0;
        while (pos + dlzkaDlzky < ramec.Length)
        {
            long dlzka = 0;
            for (var k = 0; k < dlzkaDlzky; k++)
            {
                dlzka = (dlzka << 8) | ramec[pos + k];
            }

            pos += dlzkaDlzky;
            if (dlzka < 2 || pos + dlzka > ramec.Length)
            {
                return;
            }

            NalHevc(ramec.Slice(pos, (int)dlzka), s);
            pos += (int)dlzka;
        }
    }

    /// <summary>One NAL unit of HEVC: an SEI with HDR10+, or an RPU of Dolby Vision.</summary>
    internal static void NalHevc(ReadOnlySpan<byte> nal, InfoStopy s)
    {
        if (nal.Length < 3)
        {
            return;
        }

        var typ = (nal[0] >> 1) & 0x3F;
        if (typ is 39 or 40 && Hdr10Plus(CitacBitov.BezEmulacie(nal[2..])))
        {
            s.Hdr10Plus = true;
        }
        else if (typ == 62 && s.Hdr != Hdr.DolbyVision)
        {
            s.Hdr = Hdr.DolbyVision;
            s.HdrPopis ??= "Dolby Vision (RPU v obraze)";
        }
    }

    /// <summary>An SEI message of user data registered by ITU-T T.35: country 0xB5, provider 0x003C, application 4.</summary>
    private static bool Hdr10Plus(byte[] sei)
    {
        var i = 0;
        while (i < sei.Length - 2)
        {
            var typ = 0;
            while (i < sei.Length && sei[i] == 0xFF)
            {
                typ += 255;
                i++;
            }

            if (i >= sei.Length)
            {
                return false;
            }

            typ += sei[i++];
            var velkost = 0;
            while (i < sei.Length && sei[i] == 0xFF)
            {
                velkost += 255;
                i++;
            }

            if (i >= sei.Length)
            {
                return false;
            }

            velkost += sei[i++];
            if (typ == 4 && velkost >= 6 && i + 6 <= sei.Length &&
                sei[i] == 0xB5 && sei[i + 1] == 0x00 && sei[i + 2] == 0x3C && sei[i + 3] == 0x00 && sei[i + 4] == 0x01 && sei[i + 5] == 0x04)
            {
                return true;
            }

            i += velkost;
        }

        return false;
    }

    private static int Najdi(ReadOnlySpan<byte> data, ReadOnlySpan<byte> vzor) => data.IndexOf(vzor);

    /// <summary>"-24 dB".</summary>
    public static string Decibely(int db) => db.ToString(CultureInfo.InvariantCulture) + " dB";
}
