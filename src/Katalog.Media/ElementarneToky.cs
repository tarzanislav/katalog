using System.Globalization;
using Katalog.Jadro.Model;

namespace Katalog.Media;

/// <summary>
/// The start of an elementary stream as the transport and program streams (TS, M2TS, VOB, MPG) and AVI carry it,
/// without a container's description: the picture's sequence header (H.264, HEVC, VVC, MPEG-2, VC-1) and the first
/// audio frame (AC-3, E-AC-3, TrueHD, DTS, LPCM of Blu-ray, AAC ADTS, MPEG audio) tell the codec's details.
/// </summary>
public static class ElementarneToky
{
    /// <summary>The first bytes of a stream by its codec; fills what the container did not say.</summary>
    public static void Analyzuj(ReadOnlySpan<byte> data, InfoStopy s)
    {
        switch (s.Kodek)
        {
            case "h264":
                foreach (var nal in NalyAnnexB(data))
                {
                    if ((nal[0] & 0x1F) == 7)
                    {
                        KodekyVidea.SpsAvc(nal, s);
                        break;
                    }
                }

                break;
            case "hevc":
                var sps = false;
                foreach (var nal in NalyAnnexB(data))
                {
                    var typ = (nal[0] >> 1) & 0x3F;
                    if (typ == 33 && !sps)
                    {
                        KodekyVidea.SpsHevc(nal, s);
                        sps = true;
                    }
                    else if (typ is 39 or 40 or 62)
                    {
                        AnalyzaRamcov.NalHevc(nal, s);
                    }
                }

                break;
            case "mpeg2video" or "mpeg1video":
                Mpeg2(data, s);
                break;
            case "vc1":
                Vc1(data, s);
                break;
            case "aac":
                Adts(data, s);
                break;
            case "mp3" or "mp2" or "mp1":
                MpegAudio(data, s);
                break;
            case "pcm_bluray":
                LpcmBluray(data, s);
                break;
            case "pcm_dvd" when data.Length >= 6:
                // frames, first access unit, emphasis/mute/frame, quantization/frequency/channels, dynamic range
                s.BitovaHlbka ??= (data[4] >> 6) switch { 0 => 16, 1 => 20, _ => 24 };
                s.Vzorkovanie ??= ((data[4] >> 4) & 3) == 1 ? 96000 : 48000;
                s.Kanaly ??= (data[4] & 7) + 1;
                s.KonstantnyTok = true;
                break;
            case "ac3" or "eac3" or "truehd" or "dts" or "dts-hd ma" or "dts-hd hra" or "dts:x":
                AnalyzaRamcov.Zvuk(data, s);
                break;
        }
    }

    /// <summary>The NAL units of an Annex B stream (start codes 00 00 01 / 00 00 00 01), without the start codes.</summary>
    public static IEnumerable<byte[]> NalyAnnexB(ReadOnlySpan<byte> data)
    {
        var naly = new List<byte[]>();
        var zaciatok = -1;
        for (var i = 0; i + 2 < data.Length; i++)
        {
            if (data[i] != 0 || data[i + 1] != 0 || data[i + 2] != 1)
            {
                continue;
            }

            if (zaciatok >= 0)
            {
                var koniec = i > 0 && data[i - 1] == 0 ? i - 1 : i;
                if (koniec > zaciatok)
                {
                    naly.Add(data[zaciatok..koniec].ToArray());
                }
            }

            zaciatok = i + 3;
            i += 2;
            if (naly.Count > 256)
            {
                break;
            }
        }

        if (zaciatok >= 0 && zaciatok < data.Length && naly.Count <= 256)
        {
            naly.Add(data[zaciatok..].ToArray());
        }

        return naly.Where(n => n.Length > 1);
    }

    // ---------------------------------------------------------------- MPEG-2 and VC-1 video

    private static readonly double[] SnimkyMpeg2 = [0, 24000 / 1001.0, 24, 25, 30000 / 1001.0, 30, 50, 60000 / 1001.0, 60];

    /// <summary>The sequence header (00 00 01 B3) and its extension (B5, id 1): size, aspect, frame rate, profile and level.</summary>
    internal static void Mpeg2(ReadOnlySpan<byte> data, InfoStopy s)
    {
        var i = data.IndexOf([(byte)0, (byte)0, (byte)1, (byte)0xB3]);
        if (i < 0 || data.Length - i < 12)
        {
            return;
        }

        var r = new CitacBitov(data.Slice(i + 4, 8).ToArray());
        var sirka = (int)r.Bity(12);
        var vyska = (int)r.Bity(12);
        var pomer = (int)r.Bity(4);
        var snimky = (int)r.Bity(4);
        var tok = r.Bity(18);
        s.Sirka ??= sirka;
        s.Vyska ??= vyska;
        s.PomerStran ??= pomer switch { 2 => 4 / 3.0, 3 => 16 / 9.0, 4 => 2.21, _ => sirka / (double)Math.Max(1, vyska) };
        if (snimky is > 0 and < 9)
        {
            s.Snimky ??= SnimkyMpeg2[snimky];
            s.KonstantneSnimky ??= true;
        }

        if (tok is > 0 and < 0x3FFFF)
        {
            s.MaxTok = tok * 400;
        }

        s.FarebnyPriestor ??= "YUV";
        s.BitovaHlbka ??= 8;
        var rozsirenie = data[i..].IndexOf([(byte)0, (byte)0, (byte)1, (byte)0xB5]);
        if (rozsirenie >= 0 && i + rozsirenie + 10 <= data.Length && data[i + rozsirenie + 4] >> 4 == 1)
        {
            var e = new CitacBitov(data.Slice(i + rozsirenie + 4, 6).ToArray());
            e.Preskoc(4);
            var pl = (int)e.Bity(8);
            var progresivne = e.Bit() == 1;
            var chroma = (int)e.Bity(2);
            s.Profil ??= ((pl >> 4) & 7) switch { 5 => "Simple", 4 => "Main", 3 => "SNR", 2 => "Spatial", 1 => "High", _ => "4:2:2" } + "@" +
                         (pl & 15) switch { 10 => "Low", 8 => "Main", 6 => "High 1440", 4 => "High", _ => "?" };
            s.Podvzorkovanie ??= chroma switch { 2 => "4:2:2", 3 => "4:4:4", _ => "4:2:0" };
            s.Prekladane ??= !progresivne;
            s.Kodek = "mpeg2video";
            s.FormatInfo ??= "MPEG-2";
        }
        else
        {
            s.Kodek = "mpeg1video";
            s.FormatInfo ??= "MPEG-1";
            s.Podvzorkovanie ??= "4:2:0";
            s.Prekladane ??= false;
        }
    }

    /// <summary>The sequence header of VC-1 Advanced profile (00 00 01 0F): profile, level, coded size.</summary>
    internal static void Vc1(ReadOnlySpan<byte> data, InfoStopy s)
    {
        var i = data.IndexOf([(byte)0, (byte)0, (byte)1, (byte)0x0F]);
        if (i < 0 || data.Length - i < 10)
        {
            return;
        }

        var r = new CitacBitov(data.Slice(i + 4, 6).ToArray());
        var profil = (int)r.Bity(2);
        var level = (int)r.Bity(3);
        r.Preskoc(2 + 3 + 5 + 1);
        s.Sirka ??= ((int)r.Bity(12) + 1) * 2;
        s.Vyska ??= ((int)r.Bity(12) + 1) * 2;
        s.Profil ??= (profil == 3 ? "Advanced" : profil == 1 ? "Main" : "Simple") + "@L" + level.ToString(CultureInfo.InvariantCulture);
        s.FarebnyPriestor ??= "YUV";
        s.Podvzorkovanie ??= "4:2:0";
        s.BitovaHlbka ??= 8;
    }

    // ---------------------------------------------------------------- audio

    private static readonly int[] VzorkovanieAac = [96000, 88200, 64000, 48000, 44100, 32000, 24000, 22050, 16000, 12000, 11025, 8000, 7350];

    /// <summary>The ADTS header of AAC (sync 0xFFF): object type, sample rate, channels.</summary>
    internal static void Adts(ReadOnlySpan<byte> data, InfoStopy s)
    {
        for (var i = 0; i + 7 <= data.Length; i++)
        {
            if (data[i] != 0xFF || (data[i + 1] & 0xF6) != 0xF0)
            {
                continue;
            }

            var profil = (data[i + 2] >> 6) + 1;
            var index = (data[i + 2] >> 2) & 15;
            var kanaly = ((data[i + 2] & 1) << 2) | (data[i + 3] >> 6);
            if (index >= VzorkovanieAac.Length)
            {
                continue;
            }

            s.Profil ??= profil switch { 1 => "Main", 2 => "LC", 3 => "SSR", _ => "LTP" };
            s.Vzorkovanie ??= VzorkovanieAac[index];
            s.Kanaly ??= kanaly == 7 ? 8 : kanaly;
            s.FormatInfo ??= "Advanced Audio Codec (ADTS)";
            return;
        }
    }

    private static readonly int[,] TokyMpeg =
    {
        { 0, 32, 64, 96, 128, 160, 192, 224, 256, 288, 320, 352, 384, 416, 448 },
        { 0, 32, 48, 56, 64, 80, 96, 112, 128, 160, 192, 224, 256, 320, 384 },
        { 0, 32, 40, 48, 56, 64, 80, 96, 112, 128, 160, 192, 224, 256, 320 },
        { 0, 32, 48, 56, 64, 80, 96, 112, 128, 144, 160, 176, 192, 224, 256 },
        { 0, 8, 16, 24, 32, 40, 48, 56, 64, 80, 96, 112, 128, 144, 160 },
    };

    /// <summary>The header of an MPEG audio frame: version and layer, bit rate, sample rate, mono or stereo.</summary>
    internal static void MpegAudio(ReadOnlySpan<byte> data, InfoStopy s)
    {
        for (var i = 0; i + 4 <= data.Length; i++)
        {
            if (data[i] != 0xFF || (data[i + 1] & 0xE0) != 0xE0)
            {
                continue;
            }

            var verzia = (data[i + 1] >> 3) & 3;
            var vrstva = (data[i + 1] >> 1) & 3;
            var tok = data[i + 2] >> 4;
            var frekvencia = (data[i + 2] >> 2) & 3;
            if (verzia == 1 || vrstva == 0 || tok is 0 or 15 || frekvencia == 3)
            {
                continue;
            }

            var mpeg1 = verzia == 3;
            var tabulka = mpeg1 ? 3 - vrstva : vrstva == 3 ? 3 : 4;
            s.DatovyTok ??= TokyMpeg[tabulka, tok] * 1000L;
            s.KonstantnyTok ??= true;
            int[] zaklad = [44100, 48000, 32000];
            s.Vzorkovanie ??= zaklad[frekvencia] / (mpeg1 ? 1 : verzia == 2 ? 2 : 4);
            s.Kanaly ??= (data[i + 3] >> 6) == 3 ? 1 : 2;
            var nazov = (mpeg1 ? "MPEG-1" : verzia == 2 ? "MPEG-2" : "MPEG-2.5") + " Layer " + (4 - vrstva) switch { 1 => "1", 2 => "2", _ => "3" };
            s.FormatInfo = nazov;
            if (vrstva == 1)
            {
                (s.Kodek, s.ObchodnyNazov) = ("mp3", "MP3");
            }
            else
            {
                s.Kodek = vrstva == 2 ? "mp2" : "mp1";
            }

            return;
        }
    }

    /// <summary>The 4-byte header of Blu-ray LPCM in a PES: channel assignment, sample rate, bits per sample.</summary>
    internal static void LpcmBluray(ReadOnlySpan<byte> data, InfoStopy s)
    {
        if (data.Length < 4)
        {
            return;
        }

        var priradenie = data[2] >> 4;
        var frekvencia = data[2] & 15;
        var bity = data[3] >> 6;
        int[] kanaly = [0, 1, 0, 2, 3, 3, 4, 4, 5, 6, 7, 8, 0, 0, 0, 0];
        s.Kanaly ??= kanaly[priradenie] is > 0 and var k ? k : null;
        s.Vzorkovanie ??= frekvencia switch { 1 => 48000, 4 => 96000, 5 => 192000, _ => null };
        s.BitovaHlbka ??= bity switch { 1 => 16, 2 => 20, 3 => 24, _ => null };
        if (s.Kanaly is { } c && s.Vzorkovanie is { } f && s.BitovaHlbka is { } b)
        {
            s.DatovyTok ??= (long)((c % 2 == 1 ? c + 1 : c) * f * (long)b); // Blu-ray carries an even number of channels
            s.KonstantnyTok = true;
        }
    }
}
